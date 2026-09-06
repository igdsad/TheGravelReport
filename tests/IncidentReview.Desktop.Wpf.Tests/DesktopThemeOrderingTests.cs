using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class DesktopThemeOrderingTests
{
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
    [TestProperty("Requirement", "QR-TST-001")]
    public void StartupPreferenceAppliesResolvedPaletteWhileWindowIsStillHidden()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            MainWindowViewModel? viewModel = null;
            try
            {
                var service = new FakeIncidentReviewService();
                var themeController = new FakeThemeController(ResolvedTheme.Dark);
                viewModel = new MainWindowViewModel(
                    service,
                    new RecordingDispatcher(hasAccess: true),
                    themeController);
                window = new MainWindow(viewModel, themeController);

                viewModel.PrepareForStartup(TestModelFactory.Preferences(
                    theme: ThemePreference.Light));

                Assert.IsFalse(window.IsVisible);
                Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
                Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
                Assert.AreEqual(ResolvedTheme.Light, themeController.AppliedTheme);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
                viewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "The WPF startup probe did not finish.");
        failure?.Throw();
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
