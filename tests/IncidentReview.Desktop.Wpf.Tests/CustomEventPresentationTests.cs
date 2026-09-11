using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class CustomEventPresentationTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Error HostCleanupError = Error.Create(
        ErrorCode.Define("test.event-sync.host-cleanup"),
        ErrorKind.Unavailable,
        "The test event host could not be stopped.");

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SnapshotOrdersCustomEventsByReplayPositionDespiteSkewedClientClocks()
    {
        var sessionId = SessionIdentity.Generate();
        var laterSession = TestModelFactory.CustomEvent(
            sessionId,
            occurredAt: 500,
            replayTime: 1_000,
            submitter: "Third Driver",
            isSynchronized: true,
            sessionNumber: 2);
        var laterPosition = TestModelFactory.CustomEvent(
            sessionId,
            occurredAt: 1_000,
            replayTime: 15_000,
            submitter: "Second Driver",
            isSynchronized: true);
        var earlierPosition = TestModelFactory.CustomEvent(
            sessionId,
            occurredAt: 2_000,
            replayTime: 7_000,
            submitter: "First Driver");
        var session = TestModelFactory.SessionWithCustomEvents(
            sessionId,
            [laterSession, laterPosition, earlierPosition]);
        var snapshot = TestModelFactory.Snapshot(
            revision: 1,
            activeSession: session);

        var state = MainWindowReducer.Reduce(
            MainWindowReducer.InitialState,
            new MainWindowAction.ApplySnapshot(
                snapshot,
                ResolvedTheme.Light,
                SnapshotRefreshMode.Automatic,
                TestTime));

        Assert.HasCount(3, state.CustomEvents);
        Assert.IsTrue(state.HasCustomEvents);
        Assert.AreEqual(earlierPosition.Id, state.CustomEvents[0].Id);
        Assert.AreEqual("First Driver", state.CustomEvents[0].Submitter);
        Assert.AreEqual("Saved locally", state.CustomEvents[0].SynchronizationStatus);
        Assert.AreEqual(laterPosition.Id, state.CustomEvents[1].Id);
        Assert.AreEqual("Second Driver", state.CustomEvents[1].Submitter);
        Assert.AreEqual("Synced", state.CustomEvents[1].SynchronizationStatus);
        Assert.AreEqual(
            laterPosition.Id.ToString(),
            state.CustomEvents[1].FullCustomEventIdText);
        Assert.AreEqual(laterSession.Id, state.CustomEvents[2].Id);
        Assert.AreEqual("Third Driver", state.CustomEvents[2].Submitter);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AutomaticRefreshPreservesAllDirtyCustomEventDrafts()
    {
        var originalPreferences = TestModelFactory.Preferences(
            submitterName: "Stored Driver",
            customEventKey: "F9",
            eventJoinCode: "stored-code");
        var state = ApplySnapshot(
            MainWindowReducer.InitialState,
            revision: 1,
            originalPreferences,
            SnapshotRefreshMode.Automatic);
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.EditCustomEventSettings(
                "  Draft Driver  ",
                "Ctrl+Shift+F10",
                "  draft-code  "));
        Assert.IsTrue(state.IsCustomEventSettingsDirty);

        var refreshedPreferences = TestModelFactory.Preferences(
            submitterName: "Other Stored Driver",
            customEventKey: "F11",
            eventJoinCode: "other-stored-code");
        var refreshed = ApplySnapshot(
            state,
            revision: 2,
            refreshedPreferences,
            SnapshotRefreshMode.Automatic);

        Assert.AreSame(refreshedPreferences, refreshed.SavedPreferences);
        Assert.AreEqual("  Draft Driver  ", refreshed.CustomEventSubmitterDraft);
        Assert.AreEqual("Ctrl+Shift+F10", refreshed.CustomEventKeyDraft);
        Assert.AreEqual("  draft-code  ", refreshed.EventJoinCodeDraft);
        Assert.IsTrue(refreshed.IsCustomEventSettingsDirty);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-EVT-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SavingCustomEventSettingsPersistsThemWithoutLosingOtherPreferences()
    {
        var stored = TestModelFactory.Preferences(
            leadIn: 5_000,
            speed: 0.75,
            autoPause: true,
            camera: "TV2",
            theme: ThemePreference.Dark,
            submitterName: "Old Driver",
            customEventKey: "F8");
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                preferences: stored)),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.CustomEventSubmitter = "  League Driver  ";
        viewModel.CustomEventKey = "Ctrl+Shift+F10";
        viewModel.EventJoinCode = "  join-code-v1  ";

        await viewModel.SaveCustomEventSettingsAsync(CancellationToken.None);

        Assert.AreEqual(1, service.UpdatePreferencesCalls);
        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(5_000, service.UpdatedPreferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(0.75, service.UpdatedPreferences.PlaybackSpeed);
        Assert.IsTrue(service.UpdatedPreferences.AutoPause);
        Assert.AreEqual("TV2", service.UpdatedPreferences.PreferredCamera);
        Assert.AreEqual(ThemePreference.Dark, service.UpdatedPreferences.Theme);
        Assert.AreEqual("League Driver", service.UpdatedPreferences.SubmitterName);
        Assert.AreEqual("Ctrl+Shift+F10", service.UpdatedPreferences.CustomEventKey);
        Assert.AreEqual("join-code-v1", service.UpdatedPreferences.EventJoinCode);
        Assert.AreSame(service.UpdatedPreferences, viewModel.State.SavedPreferences);
        Assert.IsFalse(viewModel.State.IsCustomEventSettingsDirty);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task CreatingCustomEventInvokesTheServiceAndRefreshesTheSnapshot()
    {
        var sessionId = SessionIdentity.Generate();
        var initialSession = TestModelFactory.Session(sessionId);
        var created = TestModelFactory.CustomEvent(
            sessionId,
            occurredAt: 2_000,
            replayTime: 15_000,
            submitter: "League Driver");
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                activeSession: initialSession)),
        };
        service.CreateCustomEventHandler = _ =>
        {
            service.SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 2,
                activeSession: TestModelFactory.SessionWithCustomEvents(
                    sessionId,
                    [created])));
            return Task.FromResult(Result.Success());
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.IsTrue(viewModel.CreateCustomEventCommand.CanExecute(parameter: null));

        await viewModel.CreateCustomEventAsync(CancellationToken.None);

        Assert.AreEqual(1, service.CreateCustomEventCalls);
        Assert.AreEqual(2, service.SnapshotCalls);
        Assert.HasCount(1, viewModel.State.CustomEvents);
        Assert.AreEqual(created.Id, viewModel.State.CustomEvents[0].Id);
        Assert.AreEqual("Custom review event saved locally.", viewModel.State.StatusDetail);
        Assert.AreSame(viewModel.State.EventLog[^1], viewModel.State.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedServerActionCreatesAndPersistsTheJoinCode()
    {
        var sessionId = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("v1:subsession:123:session:2").Value);
        var preferences = TestModelFactory.Preferences(
            submitterName: "League Driver",
            customEventKey: "F9");
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                activeSession: TestModelFactory.Session(sessionId),
                preferences: preferences)),
            CreateCustomEventJoinCodeResult = Result<string>.Success("grv1_shared-test"),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.EventHostAddress = " http://138.197.164.63:5088/ ";
        Assert.IsTrue(viewModel.UseSharedEventServerCommand.CanExecute(parameter: null));

        viewModel.UseSharedEventServerCommand.Execute(parameter: null);

        await WaitUntilAsync(() =>
            service.UpdatePreferencesCalls == 1 && !viewModel.State.IsBusy);
        Assert.AreEqual(1, service.CreateCustomEventJoinCodeCalls);
        Assert.AreEqual(
            new Uri("http://138.197.164.63:5088/"),
            service.SharedEventServerBaseUri);
        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual("League Driver", service.UpdatedPreferences.SubmitterName);
        Assert.AreEqual("F9", service.UpdatedPreferences.CustomEventKey);
        Assert.AreEqual("grv1_shared-test", service.UpdatedPreferences.EventJoinCode);
        Assert.IsFalse(viewModel.State.IsHostingCustomEventSession);
        Assert.AreEqual(
            "Shared-server join code created. Copy it for the other drivers.",
            viewModel.State.StatusDetail);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task FailedHostPreferenceSaveStopsTheStartedSession()
    {
        var sessionId = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("v1:subsession:123:session:2").Value);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                activeSession: TestModelFactory.Session(sessionId),
                preferences: TestModelFactory.Preferences(
                    submitterName: "League Driver",
                    customEventKey: "F9"))),
            UpdatePreferencesResult = Result.Failure(ApplicationErrors.EventSyncUnavailable),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.ToggleCustomEventSessionCommand.Execute(parameter: null);

        await WaitUntilAsync(() =>
            service.StopCustomEventSessionCalls == 1 && !viewModel.State.IsBusy);
        Assert.AreEqual(1, service.StartCustomEventSessionCalls);
        Assert.AreEqual(1, service.UpdatePreferencesCalls);
        Assert.AreEqual(1, service.StopCustomEventSessionCalls);
        Assert.IsFalse(viewModel.State.IsHostingCustomEventSession);
        Assert.AreEqual(
            ApplicationErrors.EventSyncUnavailable.Message,
            viewModel.State.StatusDetail);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task FailedHostCleanupLeavesTheStopControlAvailable()
    {
        var sessionId = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("v1:subsession:456:session:2").Value);
        var service = new FakeIncidentReviewService
        {
            SnapshotResult = Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot(
                revision: 1,
                activeSession: TestModelFactory.Session(sessionId),
                preferences: TestModelFactory.Preferences(
                    submitterName: "League Driver",
                    customEventKey: "F9"))),
            UpdatePreferencesResult = Result.Failure(ApplicationErrors.EventSyncUnavailable),
            StopCustomEventSessionResult = Result.Failure(HostCleanupError),
        };
        await using var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.ToggleCustomEventSessionCommand.Execute(parameter: null);

        await WaitUntilAsync(() =>
            service.StopCustomEventSessionCalls == 1 && !viewModel.State.IsBusy);
        Assert.AreEqual(1, service.StartCustomEventSessionCalls);
        Assert.AreEqual(1, service.UpdatePreferencesCalls);
        Assert.IsTrue(viewModel.State.IsHostingCustomEventSession);
        Assert.AreEqual("Stop session", viewModel.State.CustomEventSessionActionText);
        Assert.AreEqual(HostCleanupError.Message, viewModel.State.StatusDetail);
    }

    private static MainWindowState ApplySnapshot(
        MainWindowState state,
        long revision,
        UserPreferences preferences,
        SnapshotRefreshMode refreshMode) => MainWindowReducer.Reduce(
            state,
            new MainWindowAction.ApplySnapshot(
                TestModelFactory.Snapshot(revision: revision, preferences: preferences),
                ResolvedTheme.Light,
                refreshMode,
                TestTime));

    private static MainWindowViewModel CreateViewModel(FakeIncidentReviewService service) => new(
        service,
        new RecordingDispatcher(),
        new FakeThemeController());

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
