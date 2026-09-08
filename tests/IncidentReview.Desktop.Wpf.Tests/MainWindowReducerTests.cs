using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class MainWindowReducerTests
{
    private static readonly DateTimeOffset TestTime = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedDriverFallbacks =
        ["Driver Name", "Team Name", "Car #03", "Unknown driver"];

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public void InitialStateHasOneExplicitCurrentCameraChoice()
    {
        var state = MainWindowReducer.InitialState;

        Assert.IsFalse(state.IsInitialized);
        Assert.AreEqual("Starting", state.ConnectionStatus);
        Assert.HasCount(1, state.CameraChoices);
        Assert.IsNull(state.CameraChoices[0].CameraName);
        Assert.AreEqual("Current iRacing camera", state.CameraChoices[0].DisplayName);
        Assert.AreSame(state.CameraChoices[0], state.SelectedCameraChoice);
        Assert.HasCount(0, state.EventLog);
        Assert.IsNull(state.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-UI-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EquivalentSelectorActionsAreIdentityTransitions()
    {
        var sessionId = SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(
                revision: 1,
                session: TestModelFactory.Session(sessionId, incident),
                preferences: TestModelFactory.Preferences(camera: "Cockpit"),
                cameras: ["Cockpit", "TV1"]),
            SnapshotRefreshMode.Automatic);
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectIncident(incident.Id));

        var repeatedIncident = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectIncident(incident.Id));
        var repeatedCamera = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectCamera("Cockpit"));

        Assert.AreSame(state, repeatedIncident);
        Assert.AreSame(state, repeatedCamera);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public void ManualSnapshotAtomicallyProjectsApplicationStateAndRefreshNotice()
    {
        var sessionId = SessionIdentity.Generate();
        var later = TestModelFactory.Incident(sessionId, 2_000, 15_000, 4, 2);
        var earlier = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var preferences = TestModelFactory.Preferences(camera: "Cockpit");
        var snapshot = Snapshot(
            revision: 7,
            session: TestModelFactory.Session(sessionId, later, earlier),
            preferences: preferences,
            driver: "Eric Sigurdson",
            cameras: ["TV1", "Cockpit"],
            currentCamera: "TV1");

        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            snapshot,
            SnapshotRefreshMode.Manual);

        Assert.IsTrue(state.IsInitialized);
        Assert.AreEqual(7, state.Revision);
        Assert.AreEqual(ReviewServiceStatus.Connected, state.Status);
        Assert.IsNull(state.StatusError);
        Assert.AreSame(preferences, state.SavedPreferences);
        Assert.AreSame(snapshot.ActiveSession, state.ActiveSession);
        Assert.HasCount(2, state.Incidents);
        Assert.AreEqual(earlier.Id, state.Incidents[0].Id);
        Assert.AreEqual(later.Id, state.Incidents[1].Id);
        Assert.AreEqual("Eric Sigurdson", state.ActiveDriverName);
        Assert.IsTrue(state.HasActiveDriver);
        Assert.AreEqual("Cockpit", state.SelectedCameraChoice.CameraName);
        Assert.IsFalse(state.IsCameraSelectionDirty);
        Assert.AreEqual("Refreshed incident log: 2 incidents loaded.", state.StatusDetail);
        Assert.AreSame(state.EventLog[^1], state.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SameMinuteIncidentsShowDriversAndFullTimestampsInMillisecondOrder()
    {
        var sessionId = SessionIdentity.Generate();
        var minute = new DateTimeOffset(2026, 9, 6, 20, 3, 0, TimeSpan.Zero);
        var earlierMilliseconds = minute.AddSeconds(13).AddMilliseconds(123)
            .ToUnixTimeMilliseconds();
        var laterMilliseconds = minute.AddSeconds(13).AddMilliseconds(987)
            .ToUnixTimeMilliseconds();
        var later = TestModelFactory.Incident(
            sessionId,
            laterMilliseconds,
            replayTime: 13_383,
            total: 4,
            delta: 4,
            participant: TestModelFactory.Participant(
                identity: "car-index:9:team:92",
                driverName: "Driver Two"));
        var earlier = TestModelFactory.Incident(
            sessionId,
            earlierMilliseconds,
            replayTime: 13_383,
            total: 4,
            delta: 4,
            participant: TestModelFactory.Participant(
                identity: "car-index:4:team:41",
                driverName: "Driver One"));

        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(
                revision: 1,
                session: TestModelFactory.Session(sessionId, later, earlier),
                driver: "Active Driver"),
            SnapshotRefreshMode.Automatic);

        Assert.AreEqual("Active Driver", state.ActiveDriverName);
        Assert.AreEqual(earlier.Id, state.Incidents[0].Id);
        Assert.AreEqual(later.Id, state.Incidents[1].Id);
        Assert.AreEqual("Driver One", state.Incidents[0].DriverText);
        Assert.AreEqual("Driver Two", state.Incidents[1].DriverText);
        Assert.AreEqual(
            DateTimeOffset.FromUnixTimeMilliseconds(earlierMilliseconds)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            state.Incidents[0].ObservedAtText);
        Assert.AreNotEqual(
            state.Incidents[0].ObservedAtText,
            state.Incidents[1].ObservedAtText);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-005")]
    public void DriverTextFallsBackToTeamCarAndUnknownContext()
    {
        var sessionId = SessionIdentity.Generate();
        var driver = TestModelFactory.Incident(
            sessionId,
            observedAt: 1_000,
            replayTime: 1_000,
            total: 1,
            delta: 1,
            participant: TestModelFactory.Participant(
                identity: "driver",
                driverName: "Driver Name",
                teamName: "Team Name",
                carNumber: "01"));
        var team = TestModelFactory.Incident(
            sessionId,
            observedAt: 2_000,
            replayTime: 2_000,
            total: 2,
            delta: 1,
            participant: TestModelFactory.Participant(
                identity: "team",
                driverName: null,
                teamName: "Team Name",
                carNumber: "02"));
        var car = TestModelFactory.Incident(
            sessionId,
            observedAt: 3_000,
            replayTime: 3_000,
            total: 3,
            delta: 1,
            participant: TestModelFactory.Participant(
                identity: "car",
                driverName: null,
                teamName: null,
                carNumber: "03"));
        var unknown = TestModelFactory.Incident(
            sessionId,
            observedAt: 4_000,
            replayTime: 4_000,
            total: 4,
            delta: 1,
            participant: TestModelFactory.Participant(
                identity: "unknown",
                driverName: null,
                teamName: null,
                carNumber: null));

        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(
                revision: 1,
                session: TestModelFactory.Session(sessionId, driver, team, car, unknown)),
            SnapshotRefreshMode.Automatic);

        CollectionAssert.AreEqual(
            ExpectedDriverFallbacks,
            state.Incidents.Select(static incident => incident.DriverText).ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public void NoticeUsesOneEventInstanceForTheLogAndStatus()
    {
        var state = MainWindowReducer.Reduce(
            MainWindowReducer.InitialState,
            new MainWindowAction.ShowNotice(TestTime, "Replay is ready.", isError: false));

        Assert.HasCount(1, state.EventLog);
        Assert.AreSame(state.EventLog[0], state.CurrentStatusEvent);
        Assert.AreEqual(state.EventLog[0].Message, state.StatusDetail);
        Assert.IsFalse(state.HasError);

        var busy = MainWindowReducer.Reduce(state, new MainWindowAction.SetBusy(isBusy: true));
        Assert.AreEqual("Working…", busy.StatusDetail);
        Assert.AreSame(state.CurrentStatusEvent, busy.CurrentStatusEvent);
        Assert.AreSame(state.Incidents, busy.Incidents);
        Assert.AreSame(state.CameraChoices, busy.CameraChoices);
        Assert.AreSame(state.EventLog, busy.EventLog);

        var idle = MainWindowReducer.Reduce(busy, new MainWindowAction.SetBusy(isBusy: false));
        Assert.AreEqual("Replay is ready.", idle.StatusDetail);
        Assert.AreSame(state.CurrentStatusEvent, idle.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public void FreshUnavailableSnapshotDisplaysItsExactErrorWithoutAnEarlierUpdate()
    {
        var snapshot = Snapshot(
            revision: 1,
            status: ReviewServiceStatus.Unavailable,
            statusError: ApplicationErrors.ReplayUnavailable);

        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            snapshot,
            SnapshotRefreshMode.Automatic);

        Assert.AreSame(ApplicationErrors.ReplayUnavailable, state.StatusError);
        Assert.IsTrue(state.HasError);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, state.StatusDetail);
        Assert.HasCount(1, state.EventLog);
        Assert.AreSame(state.EventLog[0], state.CurrentStatusEvent);
        Assert.IsTrue(state.EventLog[0].IsError);

        var repeated = ReduceSnapshot(state, snapshot, SnapshotRefreshMode.Automatic);
        Assert.HasCount(1, repeated.EventLog);
        Assert.AreSame(repeated.EventLog[0], repeated.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public void LowerRevisionIsIgnoredButEqualRevisionCanRefreshTransientContext()
    {
        var current = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(revision: 5, driver: "First", cameras: ["TV1"]),
            SnapshotRefreshMode.Automatic);

        var stale = ReduceSnapshot(
            current,
            Snapshot(revision: 4, driver: "Stale", cameras: ["Cockpit"]),
            SnapshotRefreshMode.Manual);
        Assert.AreSame(current, stale);

        var equal = ReduceSnapshot(
            current,
            Snapshot(revision: 5, driver: "Updated", cameras: ["TV2"]),
            SnapshotRefreshMode.Automatic);
        Assert.AreEqual("Updated", equal.ActiveDriverName);
        Assert.IsTrue(equal.CameraChoices.Any(choice => choice.CameraName == "TV2"));
        Assert.IsFalse(equal.CameraChoices.Any(choice => choice.CameraName == "TV1"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public void AutomaticSnapshotPreservesDirtyCameraAndIncidentSelection()
    {
        var sessionId = SessionIdentity.Generate();
        var selected = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var firstSnapshot = Snapshot(
            revision: 1,
            session: TestModelFactory.Session(sessionId, selected),
            preferences: TestModelFactory.Preferences(camera: "Cockpit"),
            cameras: ["Cockpit", "TV1"]);
        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            firstSnapshot,
            SnapshotRefreshMode.Manual);
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectIncident(selected.Id));
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectCamera("TV1"));

        var added = TestModelFactory.Incident(sessionId, 2_000, 12_000, 4, 2);
        var refreshedPreferences = TestModelFactory.Preferences(camera: "Cockpit");
        var refreshed = ReduceSnapshot(
            state,
            Snapshot(
                revision: 2,
                session: TestModelFactory.Session(sessionId, selected, added),
                preferences: refreshedPreferences,
                cameras: ["TV1"]),
            SnapshotRefreshMode.Automatic);

        Assert.AreSame(refreshedPreferences, refreshed.SavedPreferences);
        Assert.AreEqual(selected.Id, refreshed.SelectedIncidentId);
        Assert.AreEqual("TV1", refreshed.SelectedCameraChoice.CameraName);
        Assert.IsTrue(refreshed.IsCameraSelectionDirty);
        Assert.IsTrue(refreshed.CameraChoices.Any(
            choice => choice.CameraName == "Cockpit" && choice.IsSavedUnavailable));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public void SavedUnavailableCameraRemainsSelectedAlongsideCurrentChoice()
    {
        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(
                revision: 1,
                preferences: TestModelFactory.Preferences(camera: "Broadcast"),
                cameras: ["TV1", "TV2"]),
            SnapshotRefreshMode.Automatic);

        Assert.IsNull(state.CameraChoices[0].CameraName);
        Assert.AreEqual("Current iRacing camera", state.CameraChoices[0].DisplayName);
        Assert.AreEqual("Broadcast", state.SelectedCameraChoice.CameraName);
        Assert.IsFalse(state.SelectedCameraChoice.IsAvailable);
        Assert.IsTrue(state.SelectedCameraChoice.IsSavedUnavailable);
        Assert.AreSame(
            state.CameraChoices.Single(choice => choice.CameraName == "Broadcast"),
            state.SelectedCameraChoice);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public void AutomaticSnapshotClearsDirtyCameraWhenDurablePreferenceCatchesUp()
    {
        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(revision: 1, cameras: ["TV1", "TV2"]),
            SnapshotRefreshMode.Automatic);
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.SelectCamera("TV2"));
        Assert.IsTrue(state.IsCameraSelectionDirty);

        state = ReduceSnapshot(
            state,
            Snapshot(
                revision: 2,
                preferences: TestModelFactory.Preferences(camera: "TV2"),
                cameras: ["TV1", "TV2"]),
            SnapshotRefreshMode.Automatic);

        Assert.AreEqual("TV2", state.SelectedCameraChoice.CameraName);
        Assert.IsFalse(state.IsCameraSelectionDirty);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public void ManualRefreshSupersedesAnOlderActivityInLogAndStatus()
    {
        var snapshot = Snapshot(revision: 1);
        var state = ReduceSnapshot(
            MainWindowReducer.InitialState,
            snapshot,
            SnapshotRefreshMode.Manual);
        state = MainWindowReducer.Reduce(
            state,
            new MainWindowAction.ShowNotice(TestTime.AddSeconds(1), "Replay is ready.", false));

        state = ReduceSnapshot(
            state,
            snapshot,
            SnapshotRefreshMode.Manual,
            TestTime.AddSeconds(2));

        Assert.AreEqual("Refreshed incident log: 0 incidents loaded.", state.StatusDetail);
        Assert.AreSame(state.EventLog[^1], state.CurrentStatusEvent);
        Assert.AreEqual(state.EventLog[^1].Message, state.StatusDetail);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public void EventLogEvictsOldestEntriesAtCapacity()
    {
        var state = MainWindowReducer.InitialState;
        for (var index = 0; index < 205; index++)
        {
            state = MainWindowReducer.Reduce(
                state,
                new MainWindowAction.ShowNotice(
                    TestTime.AddSeconds(index),
                    $"Event {index}",
                    isError: false));
        }

        Assert.HasCount(200, state.EventLog);
        Assert.AreEqual("Event 5", state.EventLog[0].Message);
        Assert.AreEqual("Event 204", state.EventLog[^1].Message);
        Assert.AreSame(state.EventLog[^1], state.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public void ConnectedSnapshotReplacesUnavailableErrorWithRecoveryEvent()
    {
        var unavailable = ReduceSnapshot(
            MainWindowReducer.InitialState,
            Snapshot(
                revision: 1,
                status: ReviewServiceStatus.Unavailable,
                statusError: ApplicationErrors.ReplayUnavailable),
            SnapshotRefreshMode.Automatic);

        var connected = ReduceSnapshot(
            unavailable,
            Snapshot(revision: 2),
            SnapshotRefreshMode.Automatic,
            TestTime.AddSeconds(1));

        Assert.IsNull(connected.StatusError);
        Assert.IsFalse(connected.HasError);
        Assert.AreEqual(
            "Simulator status changed: Connected to iRacing.",
            connected.StatusDetail);
        Assert.AreSame(connected.EventLog[^1], connected.CurrentStatusEvent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void ThemePreferenceAndResolvedPaletteEnterStateTogether()
    {
        var darkPreferences = TestModelFactory.Preferences(theme: ThemePreference.Dark);

        var state = MainWindowReducer.Reduce(
            MainWindowReducer.InitialState,
            new MainWindowAction.ApplyPreferences(darkPreferences, ResolvedTheme.Dark));

        Assert.AreSame(darkPreferences, state.SavedPreferences);
        Assert.AreEqual(ThemePreference.Dark, state.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, state.ResolvedTheme);
        Assert.IsTrue(state.UsesDarkThemePreference);
        Assert.IsFalse(state.UsesLightThemePreference);
        Assert.IsFalse(state.FollowsDesktopTheme);
        Assert.AreEqual("Theme: Dark. Click to follow desktop.", state.ThemeToolTip);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void DesktopThemeChangesOnlyAffectFollowDesktopState()
    {
        var following = MainWindowReducer.Reduce(
            MainWindowReducer.InitialState,
            new MainWindowAction.DesktopThemeChanged(ResolvedTheme.Dark));

        Assert.AreEqual(ResolvedTheme.Dark, following.ResolvedTheme);

        var forcedLight = MainWindowReducer.Reduce(
            following,
            new MainWindowAction.ApplyPreferences(
                TestModelFactory.Preferences(theme: ThemePreference.Light),
                ResolvedTheme.Light));
        var afterDesktopChange = MainWindowReducer.Reduce(
            forcedLight,
            new MainWindowAction.DesktopThemeChanged(ResolvedTheme.Dark));

        Assert.AreSame(forcedLight, afterDesktopChange);
        Assert.AreEqual(ResolvedTheme.Light, afterDesktopChange.ResolvedTheme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void SnapshotAtomicallyAppliesStoredThemeAndResolvedPalette()
    {
        var snapshot = Snapshot(
            revision: 1,
            preferences: TestModelFactory.Preferences(theme: ThemePreference.Dark));

        var state = MainWindowReducer.Reduce(
            MainWindowReducer.InitialState,
            new MainWindowAction.ApplySnapshot(
                snapshot,
                ResolvedTheme.Dark,
                SnapshotRefreshMode.Automatic,
                TestTime));

        Assert.AreSame(snapshot.Preferences, state.SavedPreferences);
        Assert.AreEqual(ThemePreference.Dark, state.ConfiguredThemePreference);
        Assert.AreEqual(ResolvedTheme.Dark, state.ResolvedTheme);
    }

    private static MainWindowState ReduceSnapshot(
        MainWindowState state,
        ReviewSnapshot snapshot,
        SnapshotRefreshMode refreshMode,
        DateTimeOffset? occurredAt = null) => MainWindowReducer.Reduce(
            state,
            new MainWindowAction.ApplySnapshot(
                snapshot,
                Resolve(snapshot.Preferences.Theme),
                refreshMode,
                occurredAt ?? TestTime));

    private static ResolvedTheme Resolve(ThemePreference preference) => preference switch
    {
        ThemePreference.FollowDesktop => ResolvedTheme.Light,
        ThemePreference.Light => ResolvedTheme.Light,
        ThemePreference.Dark => ResolvedTheme.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(preference)),
    };

    private static ReviewSnapshot Snapshot(
        long revision,
        ReviewServiceStatus status = ReviewServiceStatus.Connected,
        IncidentReview.Results.Error? statusError = null,
        ReviewSession? session = null,
        UserPreferences? preferences = null,
        string? driver = null,
        string[]? cameras = null,
        string? currentCamera = null) => ReviewSnapshot.Create(
            revision,
            status,
            statusError,
            session,
            preferences ?? TestModelFactory.Preferences(camera: null),
            driver,
            cameras ?? [],
            currentCamera);
}
