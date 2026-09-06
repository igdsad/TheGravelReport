using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    private static readonly string[] ExpectedCameraDisplayNames =
        ["Current iRacing camera", "Cockpit", "TV1"];

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task RefreshRendersOneCurrentSnapshotChronologically()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var later = TestModelFactory.Incident(sessionId, 2_000, 15_000, 4, 2);
        var earlier = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var session = TestModelFactory.Session(sessionId, later, earlier);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 4,
            activeSession: session,
            driverDisplayName: "Eric Sigurdson",
            cameraGroups: ["Cockpit", "TV1"],
            currentCameraGroup: "Cockpit"));
        var dispatcher = new RecordingDispatcher();
        await using var viewModel = CreateViewModel(service, dispatcher);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(1, service.SnapshotCalls);
        Assert.AreEqual(0, service.StatusCalls);
        Assert.AreEqual("Connected to iRacing", viewModel.State.ConnectionStatus);
        Assert.AreEqual("Eric Sigurdson", viewModel.State.ActiveDriverName);
        Assert.HasCount(2, viewModel.State.Incidents);
        Assert.AreEqual(earlier.Id, viewModel.State.Incidents[0].Id);
        Assert.AreEqual(later.Id, viewModel.State.Incidents[1].Id);
        CollectionAssert.AreEqual(
            ExpectedCameraDisplayNames,
            viewModel.State.CameraChoices.Select(static choice => choice.DisplayName).ToArray());
        Assert.IsGreaterThan(0, dispatcher.InvocationCount);
        Assert.IsFalse(viewModel.State.ShowsEmptyState);
        AssertCurrentStatusIsNewestEvent(viewModel);
        StringAssert.Contains(viewModel.State.StatusDetail, "2 incidents");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-UI-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SelectorWriteBackOfCurrentValuesDoesNotPublishState()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 1,
            activeSession: TestModelFactory.Session(sessionId, incident),
            preferences: TestModelFactory.Preferences(camera: "Cockpit"),
            cameraGroups: ["Cockpit", "TV1"]));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedIncident = viewModel.State.Incidents[0];
        var selectedState = viewModel.State;
        var stateNotifications = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.State))
            {
                stateNotifications++;
            }
        };

        viewModel.SelectedIncident = viewModel.SelectedIncident;
        viewModel.SelectedCameraChoice = viewModel.SelectedCameraChoice;

        Assert.AreSame(selectedState, viewModel.State);
        Assert.AreEqual(0, stateNotifications);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-UI-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task StatePublicationIgnoresTransientSelectorWriteBack()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 1,
            activeSession: TestModelFactory.Session(sessionId, incident),
            preferences: TestModelFactory.Preferences(camera: "Cockpit"),
            cameraGroups: ["Cockpit", "TV1"]));
        var dispatcher = new RecordingDispatcher(hasAccess: true);
        await using var viewModel = CreateViewModel(service, dispatcher);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedIncident = viewModel.State.Incidents[0];
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.State))
            {
                viewModel.SelectedIncident = null;
                viewModel.SelectedCameraChoice = null!;
            }
        };

        viewModel.PrepareForStartup(TestModelFactory.Preferences(
            camera: "Cockpit",
            theme: ThemePreference.Dark));

        Assert.AreEqual(incident.Id, viewModel.State.SelectedIncidentId);
        Assert.AreEqual("Cockpit", viewModel.State.SelectedCameraChoice.CameraName);
        Assert.AreEqual(ThemePreference.Dark, viewModel.State.ConfiguredThemePreference);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task NoActiveSessionShowsThePrimaryEmptyState()
    {
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                status: ReviewServiceStatus.WaitingForSimulator)),
        };
        await using var viewModel = CreateViewModel(service);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Waiting for iRacing", viewModel.State.ConnectionStatus);
        Assert.IsTrue(viewModel.State.ShowsEmptyState);
        Assert.AreEqual("No review session is available yet.", viewModel.State.EmptyMessage);
        Assert.IsFalse(viewModel.State.HasError);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task FreshUnavailableSnapshotDisplaysItsExactReasonEverywhere()
    {
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                status: ReviewServiceStatus.Unavailable,
                statusError: ApplicationErrors.ReplayUnavailable)),
        };
        await using var viewModel = CreateViewModel(service);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("iRacing unavailable", viewModel.State.ConnectionStatus);
        Assert.IsTrue(viewModel.State.HasError);
        Assert.AreEqual(
            ApplicationErrors.ReplayUnavailable.Message,
            viewModel.State.StatusDetail);
        AssertCurrentStatusIsNewestEvent(viewModel);
        Assert.IsTrue(viewModel.State.EventLog[^1].IsError);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task RowCommandsSendTheirExplicitOffsets()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var session = TestModelFactory.Session(sessionId, incident);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            activeSession: session));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        var row = viewModel.State.Incidents[0];

        await ExecuteAndWaitAsync(viewModel.ReviewBeforeCommand, row, service, 1, viewModel);
        await ExecuteAndWaitAsync(viewModel.ReviewAtCommand, row, service, 2, viewModel);
        await ExecuteAndWaitAsync(viewModel.ReviewAfterCommand, row, service, 3, viewModel);

        Assert.AreEqual(incident.Id, service.ReviewedIncident);
        CollectionAssert.AreEqual(
            new long[] { -2_000, 0, 2_000 },
            service.ReviewedOffsets.Select(static offset => offset.Milliseconds).ToArray());
        AssertCurrentStatusIsNewestEvent(viewModel);
        StringAssert.Contains(viewModel.State.StatusDetail, "2 sec after");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task ReviewFailureBecomesOneExactErrorNotice()
    {
        var service = new FakeIncidentReviewService
        {
            ReviewResult = Result.Failure(ApplicationErrors.ReplayDriverOnTrack),
        };
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            activeSession: TestModelFactory.Session(sessionId, incident)));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.ReviewIncidentAsync(
            viewModel.State.Incidents[0],
            ReplayOffset.Zero,
            CancellationToken.None);

        Assert.AreEqual(ApplicationErrors.ReplayDriverOnTrack.Message, viewModel.State.StatusDetail);
        Assert.IsTrue(viewModel.State.HasError);
        AssertCurrentStatusIsNewestEvent(viewModel);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task ManualRefreshSupersedesAStaleReviewNotice()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 2,
            activeSession: TestModelFactory.Session(sessionId, incident)));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.ReviewIncidentAsync(
            viewModel.State.Incidents[0],
            ReplayOffset.Zero,
            CancellationToken.None);
        var reviewNotice = viewModel.State.CurrentStatusEvent;

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreNotSame(reviewNotice, viewModel.State.CurrentStatusEvent);
        AssertCurrentStatusIsNewestEvent(viewModel);
        StringAssert.StartsWith(viewModel.State.StatusDetail, "Refreshed incident log:");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task SaveCameraPreservesHiddenPlaybackPreferences()
    {
        var service = new FakeIncidentReviewService();
        var stored = TestModelFactory.Preferences(
            5_000,
            0.75,
            true,
            "TV1",
            ThemePreference.Dark);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 1,
            preferences: stored,
            cameraGroups: ["TV1", "TV2"],
            currentCameraGroup: "TV1"));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedCameraChoice = viewModel.State.CameraChoices.Single(
            static choice => choice.CameraName == "TV2");

        await viewModel.SavePreferencesAsync(CancellationToken.None);

        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(5_000, service.UpdatedPreferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(0.75, service.UpdatedPreferences.PlaybackSpeed);
        Assert.IsTrue(service.UpdatedPreferences.AutoPause);
        Assert.AreEqual("TV2", service.UpdatedPreferences.PreferredCamera);
        Assert.AreEqual(ThemePreference.Dark, service.UpdatedPreferences.Theme);
        Assert.IsFalse(viewModel.State.IsCameraSelectionDirty);
        Assert.AreEqual("TV2", viewModel.State.SelectedCameraChoice.CameraName);
        AssertCurrentStatusIsNewestEvent(viewModel);
        Assert.AreEqual("Camera preference saved.", viewModel.State.StatusDetail);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task SnapshotFailureAfterCameraSaveIsNotHiddenByASuccessNotice()
    {
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                cameraGroups: ["TV1"])),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedCameraChoice = viewModel.State.CameraChoices.Single(
            static choice => choice.CameraName == "TV1");
        service.SnapshotHandler = _ => Task.FromResult(
            Result<ReviewSnapshot>.Failure(ApplicationErrors.ReplayUnavailable));

        await viewModel.SavePreferencesAsync(CancellationToken.None);

        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.State.StatusDetail);
        Assert.IsTrue(viewModel.State.HasError);
        AssertCurrentStatusIsNewestEvent(viewModel);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task ApplicationUpdateRefreshesOnlyTheAggregateSnapshot()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = SessionIdentity.Generate();
        var first = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 1,
            activeSession: TestModelFactory.Session(sessionId, first)));
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.StartMonitoringAsync(CancellationToken.None);
        var callsBeforeUpdate = service.SnapshotCalls;
        var second = TestModelFactory.Incident(sessionId, 2_000, 12_000, 4, 2);
        service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
            revision: 2,
            activeSession: TestModelFactory.Session(sessionId, first, second),
            driverDisplayName: "Eric Sigurdson",
            cameraGroups: ["TV1"]));

        await service.PublishAsync(new ReviewUpdate.IncidentChanged(sessionId, second.Id));
        await WaitUntilAsync(() => viewModel.State.Incidents.Count == 2);

        Assert.AreEqual(callsBeforeUpdate + 1, service.SnapshotCalls);
        Assert.AreEqual(0, service.StatusCalls);
        Assert.AreEqual("Eric Sigurdson", viewModel.State.ActiveDriverName);
        AssertCurrentStatusIsNewestEvent(viewModel);
        StringAssert.Contains(viewModel.State.StatusDetail, second.Id.ToString());
        await viewModel.StopMonitoringAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CancelActiveRefreshReturnsTheStateToIdle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeIncidentReviewService
        {
            SnapshotHandler = async cancellationToken =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot());
            },
        };
        await using var viewModel = CreateViewModel(service);

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await entered.Task;
        Assert.IsTrue(viewModel.State.IsBusy);
        viewModel.CancelActiveOperation();
        await refresh;

        Assert.IsFalse(viewModel.State.IsBusy);
        Assert.IsFalse(viewModel.State.HasError);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-LIF-001")]
    public async Task DisposeWaitsForAnUncooperativeActiveOperation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeIncidentReviewService
        {
            SnapshotHandler = async _ =>
            {
                entered.SetResult();
                await release.Task;
                return Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot());
            },
        };
        var viewModel = CreateViewModel(service);
        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await entered.Task;

        var dispose = viewModel.DisposeAsync().AsTask();
        Assert.IsFalse(dispose.IsCompleted);
        release.SetResult();

        await refresh;
        await dispose;
        Assert.IsFalse(viewModel.State.IsBusy);
        await viewModel.DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task IncidentIdentityIsCompactButRetainsItsFullValue()
    {
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                activeSession: TestModelFactory.Session(sessionId, incident))),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        var item = viewModel.State.Incidents[0];

        Assert.AreEqual(item.Id.ToString(), item.FullIncidentIdText);
        Assert.AreEqual($"…{item.FullIncidentIdText[^8..]}", item.IncidentIdText);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-UI-003")]
    public async Task StartupPreferencesResolveBeforeTheFirstSnapshot()
    {
        var service = new FakeIncidentReviewService();
        var themeController = new FakeThemeController(ResolvedTheme.Dark);
        await using var viewModel = CreateViewModel(
            service,
            new RecordingDispatcher(hasAccess: true),
            themeController);
        var stored = TestModelFactory.Preferences(theme: ThemePreference.Light);

        viewModel.PrepareForStartup(stored);

        Assert.IsFalse(viewModel.State.IsInitialized);
        Assert.AreSame(stored, viewModel.State.SavedPreferences);
        Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
        Assert.AreEqual(0, service.SnapshotCalls);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-UI-003")]
    public async Task ThemeCycleAppliesImmediatelyPersistsAndPreservesReplayPreferences()
    {
        var stored = TestModelFactory.Preferences(
            leadIn: 5_000,
            speed: 0.75,
            autoPause: true,
            camera: "TV1",
            theme: ThemePreference.FollowDesktop);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: stored)),
        };
        var themeController = new FakeThemeController(ResolvedTheme.Dark);
        await using var viewModel = CreateViewModel(service, themeController: themeController);
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.IsTrue(viewModel.CycleThemeCommand.CanExecute(parameter: null));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.UpdatePreferencesHandler = async (_, token) =>
        {
            entered.SetResult();
            return await release.Task.WaitAsync(token);
        };

        var firstCycle = viewModel.CycleThemeAsync(CancellationToken.None);
        await entered.Task;

        Assert.IsFalse(firstCycle.IsCompleted);
        Assert.IsTrue(viewModel.State.IsBusy);
        Assert.IsFalse(viewModel.CycleThemeCommand.CanExecute(parameter: null));
        Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
        release.SetResult(Result.Success());
        await firstCycle;

        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(5_000, service.UpdatedPreferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(0.75, service.UpdatedPreferences.PlaybackSpeed);
        Assert.IsTrue(service.UpdatedPreferences.AutoPause);
        Assert.AreEqual("TV1", service.UpdatedPreferences.PreferredCamera);
        Assert.AreEqual(ThemePreference.Light, service.UpdatedPreferences.Theme);
        Assert.AreEqual("Theme preference saved.", viewModel.State.StatusDetail);

        service.UpdatePreferencesHandler = null;
        await viewModel.CycleThemeAsync(CancellationToken.None);
        Assert.AreEqual(ThemePreference.Dark, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, viewModel.State.ResolvedTheme);

        await viewModel.CycleThemeAsync(CancellationToken.None);
        Assert.AreEqual(ThemePreference.FollowDesktop, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, viewModel.State.ResolvedTheme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task ThemePersistenceFailureRestoresPriorStateAndReportsExactError()
    {
        var stored = TestModelFactory.Preferences(theme: ThemePreference.FollowDesktop);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: stored)),
        };
        var themeController = new FakeThemeController(ResolvedTheme.Dark);
        await using var viewModel = CreateViewModel(service, themeController: themeController);
        await viewModel.RefreshAsync(CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.UpdatePreferencesHandler = async (_, token) =>
        {
            entered.SetResult();
            return await release.Task.WaitAsync(token);
        };

        var cycle = viewModel.CycleThemeAsync(CancellationToken.None);
        await entered.Task;
        themeController.SetDesktopTheme(ResolvedTheme.Light);
        release.SetResult(Result.Failure(ApplicationErrors.ReplayUnavailable));
        await cycle;

        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(ThemePreference.Light, service.UpdatedPreferences.Theme);
        Assert.AreSame(stored, viewModel.State.SavedPreferences);
        Assert.AreEqual(ThemePreference.FollowDesktop, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.State.StatusDetail);
        Assert.IsTrue(viewModel.State.HasError);
        AssertCurrentStatusIsNewestEvent(viewModel);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-UI-003")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CancelledThemeSaveRestoresPriorPreferenceWithoutInventingAnError()
    {
        var stored = TestModelFactory.Preferences(theme: ThemePreference.Dark);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: stored)),
        };
        var themeController = new FakeThemeController(ResolvedTheme.Light);
        await using var viewModel = CreateViewModel(service, themeController: themeController);
        await viewModel.RefreshAsync(CancellationToken.None);
        var statusBefore = viewModel.State.CurrentStatusEvent;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.UpdatePreferencesHandler = async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Result.Success();
        };
        using var cancellation = new CancellationTokenSource();

        var cycle = viewModel.CycleThemeAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await cycle;

        Assert.AreSame(stored, viewModel.State.SavedPreferences);
        Assert.AreEqual(ThemePreference.Dark, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, viewModel.State.ResolvedTheme);
        Assert.AreSame(statusBefore, viewModel.State.CurrentStatusEvent);
        Assert.IsFalse(viewModel.State.IsBusy);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public async Task DesktopChangesFlowThroughStateOnlyWhileFollowingDesktop()
    {
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: TestModelFactory.Preferences(
                    theme: ThemePreference.FollowDesktop))),
        };
        var themeController = new FakeThemeController(ResolvedTheme.Light);
        await using var viewModel = CreateViewModel(service, themeController: themeController);
        await viewModel.RefreshAsync(CancellationToken.None);

        themeController.SetDesktopTheme(ResolvedTheme.Dark);
        Assert.AreEqual(ResolvedTheme.Dark, viewModel.State.ResolvedTheme);

        await viewModel.CycleThemeAsync(CancellationToken.None);
        Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
        themeController.SetDesktopTheme(ResolvedTheme.Light);
        themeController.SetDesktopTheme(ResolvedTheme.Dark);

        Assert.AreEqual(ThemePreference.Light, viewModel.State.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Light, viewModel.State.ResolvedTheme);
    }

    private static async Task ExecuteAndWaitAsync(
        System.Windows.Input.ICommand command,
        IncidentListItem incident,
        FakeIncidentReviewService service,
        int expectedCalls,
        MainWindowViewModel viewModel)
    {
        Assert.IsTrue(command.CanExecute(incident));
        command.Execute(incident);
        await WaitUntilAsync(() => service.ReviewCalls == expectedCalls && !viewModel.State.IsBusy);
    }

    private static void AssertCurrentStatusIsNewestEvent(MainWindowViewModel viewModel)
    {
        Assert.IsNotEmpty(viewModel.State.EventLog);
        Assert.AreSame(viewModel.State.EventLog[^1], viewModel.State.CurrentStatusEvent);
        Assert.AreEqual(viewModel.State.EventLog[^1].Message, viewModel.State.StatusDetail);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeIncidentReviewService service,
        RecordingDispatcher? dispatcher = null,
        FakeThemeController? themeController = null) => new(
            service,
            dispatcher ?? new RecordingDispatcher(),
            themeController ?? new FakeThemeController());

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
