using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class DesktopThemeOrderingTests
{
    private const string StartupProbeEnvironmentVariable =
        "INCIDENTREVIEW_DESKTOP_STARTUP_PROBE";

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FollowDesktopResolvesLatestPaletteInsideQueuedDispatcherCallback()
    {
        var stored = TestModelFactory.Preferences(theme: ThemePreference.Dark);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: stored)),
        };
        var dispatcher = new ManualDispatcher();
        using var themeController = new FakeThemeController(ResolvedTheme.Light);
        await using var viewModel = new MainWindowViewModel(service, dispatcher, themeController);
        await PumpUntilCompleteAsync(viewModel.RefreshAsync(CancellationToken.None), dispatcher);
        Assert.AreEqual(ThemePreference.Dark, viewModel.State.ConfiguredThemePreference);

        var cycle = viewModel.CycleThemeAsync(CancellationToken.None);
        (await dispatcher.TakeNextAsync()).Execute(); // busy
        var queuedPreferenceApply = await dispatcher.TakeNextAsync();

        themeController.SetDesktopTheme(ResolvedTheme.Dark);
        queuedPreferenceApply.Execute();

        Assert.AreEqual(ThemePreference.FollowDesktop, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, viewModel.State.ResolvedTheme);
        await PumpUntilCompleteAsync(cycle, dispatcher);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    [TestProperty("Requirement", "IR-UI-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task StartupSequenceAppliesStoredPaletteBeforeApplicationRunBoundary()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(StartupProbeEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            RunStartupSequenceProbe();
            return;
        }

        await RunStartupSequenceProbeInChildProcessAsync();
    }

    private static async Task RunStartupSequenceProbeInChildProcessAsync()
    {
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "IncidentReview.Desktop.Wpf.Tests.exe");
        Assert.IsTrue(File.Exists(executable), $"Desktop test host not found: {executable}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment[StartupProbeEnvironmentVariable] = "1";
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(
            $"FullyQualifiedName={typeof(DesktopThemeOrderingTests).FullName}." +
            nameof(StartupSequenceAppliesStoredPaletteBeforeApplicationRunBoundary));
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add("Minimal");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("off");
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add("15s");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The desktop startup probe could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            Assert.Fail("The isolated WPF startup probe did not finish in 20 seconds.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.AreEqual(
            0,
            process.ExitCode,
            $"The isolated WPF startup probe exited with " +
            $"0x{unchecked((uint)process.ExitCode):X8}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{output}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{error}");
    }

    private static void RunStartupSequenceProbe()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            MainWindowViewModel? viewModel = null;
            try
            {
                var sessionId = SessionIdentity.Generate();
                var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
                var preferences = TestModelFactory.Preferences(
                    camera: "Cockpit",
                    theme: ThemePreference.Light);
                var service = new FakeIncidentReviewService
                {
                    PreferencesResult = Result<UserPreferences>.Success(preferences),
                    SnapshotResult = Result<ReviewSnapshot>.Success(
                        TestModelFactory.Snapshot(
                            revision: 1,
                            activeSession: TestModelFactory.Session(sessionId, incident),
                            preferences: preferences,
                            cameraGroups: ["Cockpit", "TV1"],
                            currentCameraGroup: "Cockpit")),
                };
                var themeController = new FakeThemeController(ResolvedTheme.Dark);
                viewModel = new MainWindowViewModel(
                    service,
                    new RecordingDispatcher(hasAccess: true),
                    themeController);
                window = new MainWindow(viewModel, themeController);
                var application = new IncidentReviewDesktopApplication(window);

                var prepared = application.PrepareForStartup(CancellationToken.None);

                Assert.IsTrue(prepared.IsSuccess);
                Assert.IsInstanceOfType<PreparedDesktopRun>(prepared.Value);
                Assert.IsFalse(window.IsVisible);
                Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
                Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
                Assert.AreEqual(ResolvedTheme.Light, themeController.AppliedTheme);

                window.Show();
                window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                var advanced = FindVisualDescendant<Expander>(window);
                Assert.IsNotNull(advanced);
                advanced.IsExpanded = true;
                window.UpdateLayout();

                viewModel.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.IsTrue(window.IsVisible);
                Assert.HasCount(1, viewModel.State.Incidents);
                Assert.HasCount(3, viewModel.State.CameraChoices);
                Assert.AreEqual("Cockpit", viewModel.State.SelectedCameraChoice.CameraName);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                viewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window?.Close();
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "The WPF startup probe did not finish.");
        failure?.Throw();
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static async Task PumpUntilCompleteAsync(Task operation, ManualDispatcher dispatcher)
    {
        while (!operation.IsCompleted)
        {
            var pendingWork = dispatcher.WaitForPendingAsync();
            if (await Task.WhenAny(operation, pendingWork) == operation)
            {
                break;
            }

            await pendingWork;
            dispatcher.TakeNext().Execute();
        }

        await operation;
    }

    private sealed class ManualDispatcher : IUiDispatcher
    {
        private readonly Channel<PendingWork> _pending = Channel.CreateUnbounded<PendingWork>();

        public bool CheckAccess() => false;

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var work = new PendingWork(action);
            if (!_pending.Writer.TryWrite(work))
            {
                throw new InvalidOperationException("The manual dispatcher queue is unavailable.");
            }

            return work.Completion;
        }

        public async Task<PendingWork> TakeNextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _pending.Reader.ReadAsync(timeout.Token);
        }

        public PendingWork TakeNext()
        {
            if (_pending.Reader.TryRead(out var work))
            {
                return work;
            }

            throw new InvalidOperationException("The manual dispatcher queue is empty.");
        }

        public async Task WaitForPendingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (!await _pending.Reader.WaitToReadAsync(timeout.Token))
            {
                throw new InvalidOperationException("The manual dispatcher queue was completed.");
            }
        }
    }

    private sealed class PendingWork
    {
        private readonly Action _action;
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingWork(Action action)
        {
            _action = action;
        }

        public Task Completion => _completion.Task;

        public void Execute()
        {
            try
            {
                _action();
                _completion.SetResult();
            }
            catch (Exception exception)
            {
                _completion.SetException(exception);
            }
        }
    }
}
