using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IncidentReview.Application.Contracts;
using IncidentReview.Application.DependencyInjection;
using IncidentReview.Domain;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Telemetry.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentReview.Application.Tests;

[TestClass]
public sealed class IncidentReviewApplicationWorkflowTests
{
    private static readonly string[] SuccessfulReplayCommandOrder =
        ["seek", "focus-player", "set-playback"];
    private static readonly string[] SeekOnlyCommandOrder = ["seek"];
    private static readonly string[] SeekAndFocusCommandOrder = ["seek", "focus-player"];
    private static readonly string[] ReplayCameraGroups = ["Cockpit", "TV1"];
    private static readonly int[] LeagueNightSessionNumbers = [2, 3];
    private static readonly int[] ExpectedDriverSwapDeltas = [4, 2, 2];
    private static readonly ParticipantIdentity DefaultParticipantIdentity =
        ParticipantIdentity.TryCreate("local-player").Value;
    private static readonly IncidentParticipant DefaultParticipant = IncidentParticipant.TryCreate(
        DefaultParticipantIdentity,
        "Test Driver",
        "Test Team",
        "01").Value;

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task SnapshotRetainsExactUnavailableErrorWithoutAnUpdateSubscription()
    {
        var context = new RecordingReplayContextReader
        {
            ContextResult = Result<ReplayContext>.Failure(TestError),
        };
        await using var host = new TestHost(replayContext: context);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var snapshot = await host.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.AreEqual(ReviewServiceStatus.Unavailable, snapshot.Value.Status);
        Assert.AreEqual(TestError, snapshot.Value.StatusError);
        Assert.AreEqual(TestError.Message, snapshot.Value.StatusError!.Message);
        Assert.IsNull(snapshot.Value.DriverDisplayName);
        Assert.IsEmpty(snapshot.Value.CameraGroups);
        Assert.AreEqual(1, context.ReadCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-003")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-UI-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task TransientTelemetryFailureRetainsActiveSessionAndIncident()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_000,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var session = host.Store.Session!.Id;
        var incident = host.Store.LastRecord!.Incident;
        var checkpoint = host.Store.Checkpoint;
        Assert.IsNotNull(checkpoint);

        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var unavailable = await host.Service.GetSnapshotAsync(CancellationToken.None);
        Assert.IsTrue(unavailable.IsSuccess);
        Assert.AreEqual(ReviewServiceStatus.Unavailable, unavailable.Value.Status);
        Assert.AreEqual(session, unavailable.Value.ActiveSession!.Id);
        Assert.HasCount(1, unavailable.Value.ActiveSession.Incidents);
        Assert.AreEqual(incident.Id, unavailable.Value.ActiveSession.Incidents[0].Id);
        Assert.AreEqual(
            incident.Participant,
            unavailable.Value.ActiveSession.Incidents[0].Participant);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 3_000,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);

        var recovered = await host.Service.GetSnapshotAsync(CancellationToken.None);
        Assert.IsTrue(recovered.IsSuccess);
        Assert.AreEqual(session, recovered.Value.ActiveSession!.Id);
        Assert.HasCount(1, recovered.Value.ActiveSession.Incidents);
        Assert.AreEqual(incident.Id, recovered.Value.ActiveSession.Incidents[0].Id);
        Assert.AreEqual(
            incident.Participant,
            recovered.Value.ActiveSession.Incidents[0].Participant);
        Assert.AreEqual(1, host.Store.EnsureSessionCount);
        Assert.AreEqual(1, host.Store.BaselineCount);
        Assert.AreEqual(1, host.Store.RecordAttemptCount);
        Assert.AreEqual(checkpoint, host.Store.Checkpoint);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(session, current.Value.Id);
        var review = await host.Service.ReviewIncidentAsync(
            incident.Id,
            ReplayOffset.Zero,
            CancellationToken.None);
        Assert.IsTrue(review.IsSuccess);
        Assert.AreEqual(1, host.Replay.SeekCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    public async Task SnapshotLoadsCurrentSessionPreferencesAndTransientReplayContextTogether()
    {
        var preferences = UserPreferences.TryCreateMilliseconds(
            replayLeadInMilliseconds: 4_000,
            playbackSpeed: 0.5,
            autoPause: false,
            preferredCamera: "TV1").Value;
        var store = new StatefulStore { Preferences = preferences };
        var context = new RecordingReplayContextReader
        {
            ContextResult = Result<ReplayContext>.Success(
                ReplayContext.TryCreate("Eric Sigurdson", ["Cockpit", "TV1"], "TV1").Value),
        };
        await using var host = new TestHost(store, replayContext: context);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        var snapshot = await host.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.IsGreaterThan(0, snapshot.Value.Revision);
        Assert.AreEqual(ReviewServiceStatus.Connected, snapshot.Value.Status);
        Assert.IsNull(snapshot.Value.StatusError);
        Assert.AreEqual(store.Session!.Id, snapshot.Value.ActiveSession!.Id);
        Assert.AreSame(preferences, snapshot.Value.Preferences);
        Assert.AreEqual("Eric Sigurdson", snapshot.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ReplayCameraGroups,
            snapshot.Value.CameraGroups.ToArray());
        Assert.AreEqual("TV1", snapshot.Value.CurrentCameraGroup);
        Assert.AreEqual(1, context.ReadCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task SnapshotClearsTransientReplayContextWhenDisconnected()
    {
        var context = new RecordingReplayContextReader
        {
            ContextResult = Result<ReplayContext>.Success(
                ReplayContext.TryCreate("Eric Sigurdson", ["TV1"], "TV1").Value),
        };
        await using var host = new TestHost(replayContext: context);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(TelemetryDisconnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.WaitingForSimulator);

        var snapshot = await host.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.AreEqual(ReviewServiceStatus.WaitingForSimulator, snapshot.Value.Status);
        Assert.IsNull(snapshot.Value.DriverDisplayName);
        Assert.IsEmpty(snapshot.Value.CameraGroups);
        Assert.IsNull(snapshot.Value.CurrentCameraGroup);
        Assert.AreEqual(0, context.ReadCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task SnapshotRetriesWhenApplicationStateChangesDuringItsStoreRead()
    {
        var store = new StatefulStore { BlockPreferencesQuery = true };
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();

        var pendingSnapshot = host.Service.GetSnapshotAsync(CancellationToken.None);
        await store.PreferencesQueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(SecondTestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        store.ReleasePreferencesQuery();

        var snapshot = await pendingSnapshot;

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.AreEqual(ReviewServiceStatus.Unavailable, snapshot.Value.Status);
        Assert.AreEqual(SecondTestError, snapshot.Value.StatusError);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task SuccessfulPreferenceUpdateAdvancesTheCompleteSnapshot()
    {
        await using var host = new TestHost();
        var before = await host.Service.GetSnapshotAsync(CancellationToken.None);
        var updated = UserPreferences.TryCreateMilliseconds(
            replayLeadInMilliseconds: 2_000,
            playbackSpeed: 0.5,
            autoPause: false,
            preferredCamera: "TV1").Value;

        var update = await host.Service.UpdatePreferencesAsync(updated, CancellationToken.None);
        var after = await host.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(update.IsSuccess);
        Assert.IsTrue(before.IsSuccess);
        Assert.IsTrue(after.IsSuccess);
        Assert.IsGreaterThan(before.Value.Revision, after.Value.Revision);
        Assert.AreSame(updated, after.Value.Preferences);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-INC-007")]
    public async Task DetectionHandlesBaselineDuplicateResetAndIncreaseWithoutFalseIncidents()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        Assert.AreEqual(0, host.Store.RecordAttemptCount);

        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 1_500));
        await host.Telemetry.PublishAsync(CreateSample(counter: 1, time: 2_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);
        Assert.AreEqual(0, host.Store.RecordAttemptCount);
        Assert.AreEqual(1, host.Store.LastBaseline!.NextCheckpoint.CounterEpoch.Value);

        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 3_000));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var recorded = host.Store.LastRecord!;
        Assert.AreEqual(3, recorded.Incident.Points.Delta);
        Assert.AreEqual(4, recorded.Incident.Points.Total);
        Assert.AreEqual(1, recorded.Incident.CounterEpoch.Value);
        Assert.AreEqual(recorded.ExpectedCheckpoint, host.Store.Baselines[^1].NextCheckpoint);
        Assert.AreEqual(recorded.Incident.Position, recorded.NextCheckpoint.LastPosition);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task LeagueNightHeatsShareOneIncidentListAndRebaselineWithoutFalsePoints()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            sessionNumber: 2,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        var leagueNight = host.Store.Session!.Id;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_000,
            sessionNumber: 2,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 100,
            sessionNumber: 3,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);
        Assert.AreEqual(1, host.Store.RecordAttemptCount);
        Assert.AreEqual(1, host.Store.LastBaseline!.NextCheckpoint.CounterEpoch.Value);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 2,
            time: 500,
            sessionNumber: 3,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 2);

        var snapshot = await host.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.AreEqual(leagueNight, snapshot.Value.ActiveSession!.Id);
        Assert.HasCount(2, snapshot.Value.ActiveSession.Incidents);
        CollectionAssert.AreEqual(
            LeagueNightSessionNumbers,
            snapshot.Value.ActiveSession.Incidents
                .Select(static incident => incident.Position.SessionNumber.Value)
                .ToArray());
        Assert.AreEqual(1, host.Store.EnsureSessionCount);
        Assert.AreEqual(1, host.Store.CheckpointQueryCount);

        var review = await host.Service.ReviewIncidentAsync(
            snapshot.Value.ActiveSession.Incidents[0].Id,
            ReplayOffset.Zero,
            CancellationToken.None);
        Assert.IsTrue(review.IsSuccess);
        Assert.AreEqual(2, host.Replay.LastSeek!.SessionNumber.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task DurableLeagueEventResumesIntoAnotherHeatAfterApplicationRestart()
    {
        var store = new StatefulStore();
        await using (var firstHeat = new TestHost(store))
        {
            await firstHeat.StartConnectedAsync();
            await firstHeat.Telemetry.PublishAsync(CreateSample(
                counter: 0,
                time: 1_000,
                scope: SimulatorIdentityScope.Durable,
                sessionNumber: 2));
            await WaitUntilAsync(() => store.BaselineCount == 1);
            await firstHeat.Telemetry.PublishAsync(CreateSample(
                counter: 4,
                time: 2_000,
                scope: SimulatorIdentityScope.Durable,
                sessionNumber: 2));
            await WaitUntilAsync(() => store.RecordAttemptCount == 1);
            await firstHeat.StopAsync();
        }

        var leagueEvent = store.Session!.Id;
        await using var secondHeat = new TestHost(store);
        await secondHeat.StartConnectedAsync();
        await secondHeat.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 100,
            scope: SimulatorIdentityScope.Durable,
            sessionNumber: 3));
        await WaitUntilAsync(() => store.BaselineCount == 2);
        await secondHeat.Telemetry.PublishAsync(CreateSample(
            counter: 2,
            time: 500,
            scope: SimulatorIdentityScope.Durable,
            sessionNumber: 3));
        await WaitUntilAsync(() => store.RecordAttemptCount == 2);

        var snapshot = await secondHeat.Service.GetSnapshotAsync(CancellationToken.None);

        Assert.IsTrue(snapshot.IsSuccess);
        Assert.AreEqual(leagueEvent, snapshot.Value.ActiveSession!.Id);
        Assert.HasCount(2, snapshot.Value.ActiveSession.Incidents);
        Assert.AreEqual(1, store.EnsureSessionCount);
        Assert.AreEqual(2, store.CheckpointQueryCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-INC-007")]
    public async Task EqualCounterIncreasesForTwoParticipantsProduceTwoIncidents()
    {
        var first = CreateParticipant("car-index:4:team:41", "Driver One", "Team One", "14");
        var second = CreateParticipant("car-index:9:team:92", "Driver Two", "Team Two", "29");
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            incidentCounters:
            [
                CreateParticipantCounter(first, 0),
                CreateParticipantCounter(second, 0),
            ]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            incidentCounters:
            [
                CreateParticipantCounter(second, 2),
                CreateParticipantCounter(first, 2),
            ]));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 2);

        CollectionAssert.AreEqual(
            new[] { first.Identity, second.Identity },
            host.Store.RecordAttempts
                .Select(record => record.Incident.Participant.Identity)
                .ToArray());
        Assert.IsTrue(host.Store.RecordAttempts.All(record =>
            record.Incident.Points.Total == 2 && record.Incident.Points.Delta == 2));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-INC-007")]
    public async Task MissingParticipantRetainsTeamCheckpointAcrossDriverSwap()
    {
        var originalDriver = CreateParticipant(
            "car-index:4:team:41",
            "Driver One",
            "Endurance Team",
            "14");
        var replacementDriver = CreateParticipant(
            "car-index:4:team:41",
            "Driver Two",
            "Endurance Team",
            "14");
        var other = CreateParticipant("car-index:9:user:92", "Other Driver", null, "29");
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            incidentCounters:
            [
                CreateParticipantCounter(originalDriver, 5),
                CreateParticipantCounter(other, 0),
            ]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_500,
            incidentCounters: [CreateParticipantCounter(other, 0)]));
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            incidentCounters:
            [
                CreateParticipantCounter(replacementDriver, 7),
                CreateParticipantCounter(other, 0),
            ]));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);

        var incident = host.Store.LastRecord!.Incident;
        Assert.AreEqual(2, incident.Points.Delta);
        Assert.AreEqual("Driver Two", incident.Participant.DriverName);
        Assert.AreEqual(originalDriver.Identity, incident.Participant.Identity);
        Assert.AreEqual(2, host.Store.BaselineCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-INC-007")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task DriverScopedSwapAndReturnResumeIndependentPureCounterStreams()
    {
        var firstDriver = CreateParticipant(
            "car-index:4:team:41:driver-user:401",
            "Driver One",
            "Endurance Team",
            "14");
        var secondDriver = CreateParticipant(
            "car-index:4:team:41:driver-user:402",
            "Driver Two",
            "Endurance Team",
            "14");
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            incidentCounters: [CreateParticipantCounter(firstDriver, 0)]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            incidentCounters: [CreateParticipantCounter(firstDriver, 4)]));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 3_000,
            incidentCounters: [CreateParticipantCounter(secondDriver, 0)]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 4_000,
            incidentCounters: [CreateParticipantCounter(secondDriver, 2)]));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 2);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 5_000,
            incidentCounters: [CreateParticipantCounter(firstDriver, 4)]));
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 6_000,
            incidentCounters: [CreateParticipantCounter(firstDriver, 6)]));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 3);

        CollectionAssert.AreEqual(
            new[] { firstDriver.Identity, secondDriver.Identity, firstDriver.Identity },
            host.Store.RecordAttempts
                .Select(static record => record.Incident.Participant.Identity)
                .ToArray());
        CollectionAssert.AreEqual(
            ExpectedDriverSwapDeltas,
            host.Store.RecordAttempts
                .Select(static record => record.Incident.Points.Delta)
                .ToArray());
        Assert.AreEqual(2, host.Store.BaselineCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    public async Task ReusedCarSlotWithNewEntrantStartsANewBaseline()
    {
        var originalEntrant = CreateParticipant(
            "car-index:4:team:41",
            "Driver One",
            "First Team",
            "14");
        var replacementEntrant = CreateParticipant(
            "car-index:4:team:42",
            "Driver Two",
            "Second Team",
            "14");
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            incidentCounters: [CreateParticipantCounter(originalEntrant, 5)]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            incidentCounters: [CreateParticipantCounter(replacementEntrant, 1)]));
        await WaitUntilAsync(() => host.Store.BaselineCount == 2);

        Assert.AreEqual(0, host.Store.RecordAttemptCount);
        CollectionAssert.AreEquivalent(
            new[] { originalEntrant.Identity, replacementEntrant.Identity },
            host.Store.Baselines
                .Select(baseline => baseline.NextCheckpoint.ParticipantIdentity)
                .ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "IR-INC-003")]
    public async Task DisconnectReconnectResolvesDurableSessionAndReloadsCheckpoint()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        var first = CreateSample(
            counter: 2,
            time: 1_000,
            scope: SimulatorIdentityScope.Durable);
        await host.Telemetry.PublishAsync(first);
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        var originalSession = host.Store.Session!.Id;

        await host.Telemetry.PublishAsync(TelemetryDisconnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.WaitingForSimulator);
        var disconnectedCurrent = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsFalse(disconnectedCurrent.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.NoCurrentSession, disconnectedCurrent.Error!.Code);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 2,
            time: 2_000,
            scope: SimulatorIdentityScope.Durable));
        await WaitUntilAsync(() => host.Store.CheckpointQueryCount >= 2);

        Assert.AreEqual(1, host.Store.EnsureSessionCount);
        Assert.AreEqual(1, host.Store.BaselineCount);
        Assert.AreEqual(originalSession, host.Store.Session.Id);
        Assert.AreEqual(0, host.Store.RecordAttemptCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-STR-001")]
    public async Task RestartReloadsEveryParticipantCheckpoint()
    {
        var first = CreateParticipant("car-index:4:team:41", "Driver One", "Team One", "14");
        var second = CreateParticipant("car-index:9:team:92", "Driver Two", "Team Two", "29");
        var store = new StatefulStore();

        await using (var recordingHost = new TestHost(store))
        {
            await recordingHost.StartConnectedAsync();
            await recordingHost.Telemetry.PublishAsync(CreateSample(
                counter: 0,
                time: 1_000,
                scope: SimulatorIdentityScope.Durable,
                incidentCounters:
                [
                    CreateParticipantCounter(first, 2),
                    CreateParticipantCounter(second, 5),
                ]));
            await WaitUntilAsync(() => store.BaselineCount == 2);
            await recordingHost.StopAsync();
        }

        await using var restartedHost = new TestHost(store);
        await restartedHost.StartConnectedAsync();
        await restartedHost.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            scope: SimulatorIdentityScope.Durable,
            incidentCounters:
            [
                CreateParticipantCounter(first, 2),
                CreateParticipantCounter(second, 7),
            ]));
        await WaitUntilAsync(() => store.RecordAttemptCount == 1);

        Assert.AreEqual(2, store.BaselineCount);
        Assert.AreEqual(second.Identity, store.LastRecord!.Incident.Participant.Identity);
        Assert.AreEqual(2, store.LastRecord.Incident.Points.Delta);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-001")]
    public async Task ReplaySamplesNeverRunTheIncidentDetector()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 8,
            time: 4_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.SessionLookupCount == 1);

        Assert.AreEqual(0, host.Store.EnsureSessionCount);
        Assert.AreEqual(0, host.Store.BaselineCount);
        Assert.AreEqual(0, host.Store.RecordAttemptCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task IdenticalReplaySamplesResolveAndNotifyOnlyOnce()
    {
        var store = new StatefulStore();
        var descriptor = CreateSample(
            counter: 0,
            time: 1_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack).Sample.Session;
        var session = StoredSession.Create(
            SessionIdentity.Generate(),
            descriptor,
            UtcInstant.TryCreateUnixMilliseconds(1_000).Value);
        store.SeedSession(session);
        await using var host = new TestHost(store);
        var collected = CollectUpdatesUntilErrorAsync(host.Service, TestError.Code);
        await host.StartConnectedAsync();

        for (var index = 0; index < 3; index++)
        {
            await host.Telemetry.PublishAsync(CreateSample(
                counter: 0,
                time: 1_000 + index,
                mode: SessionMode.Replay,
                scope: SimulatorIdentityScope.Durable,
                onTrackState: OnTrackState.NotOnTrack));
        }

        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        var updates = await collected;

        Assert.AreEqual(1, store.SessionLookupCount);
        Assert.AreEqual(
            1,
            updates.OfType<ReviewUpdate.SessionChanged>().Count(update => update.Session == session.Id));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task MatchingConnectionScopedReplayRetainsRecordedSessionAndAllowsReview()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 2_000));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var incident = host.Store.LastRecord!.Incident;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_100,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(incident.Session, current.Value.Id);

        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);

        var review = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsTrue(review.IsSuccess);
        Assert.AreEqual(1, host.Replay.SeekCount);
        Assert.AreEqual(1, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task AnotherHeatInMatchingDurableReplayUsesCachedLeagueNight()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            scope: SimulatorIdentityScope.Durable,
            sessionNumber: 2));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_000,
            scope: SimulatorIdentityScope.Durable,
            sessionNumber: 2));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var incident = host.Store.LastRecord!.Incident;
        var sessionLookups = host.Store.SessionLookupCount;
        var checkpointQueries = host.Store.CheckpointQueryCount;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 100,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack,
            sessionNumber: 3));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(incident.Session, current.Value.Id);
        Assert.AreEqual(sessionLookups, host.Store.SessionLookupCount);
        Assert.AreEqual(checkpointQueries, host.Store.CheckpointQueryCount);

        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        var review = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsTrue(review.IsSuccess);
        Assert.AreEqual(2, host.Replay.LastSeek!.SessionNumber.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task DifferentConnectionKeyDoesNotMatchConnectionScopedReplay()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 1_500));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var incident = host.Store.LastRecord!.Incident;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "different-connection"));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsFalse(current.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.NoCurrentSession, current.Error!.Code);

        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        var review = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(review.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplaySessionIdentityUnavailable, review.Error!.Code);
        Assert.AreEqual(
            ApplicationErrors.ReplaySessionIdentityUnavailable.Message,
            review.Error.Message);
        Assert.AreEqual(0, host.Replay.SeekCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task AnotherHeatInSameConnectionScopedReplayRetainsLeagueNight()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 2_000));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var incident = host.Store.LastRecord!.Incident;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_100,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionNumber: 3));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(incident.Session, current.Value.Id);

        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        var review = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsTrue(review.IsSuccess);
        Assert.AreEqual(1, host.Replay.SeekCount);
        Assert.AreEqual(1, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task UnmatchedConnectionScopedReplayPublishesCurrentSessionInvalidation()
    {
        await using var host = new TestHost();
        var collected = CollectUpdatesUntilErrorAsync(host.Service, TestError.Code);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        var session = host.Store.Session!.Id;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "different-connection",
            sessionNumber: 3));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        var updates = await collected;

        Assert.AreEqual(
            2,
            updates.OfType<ReviewUpdate.SessionChanged>().Count(update =>
                update.Session == session));
        Assert.IsFalse(
            (await host.Service.GetCurrentSessionAsync(CancellationToken.None)).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task ReturningFromDifferentReplayRestoresConnectionScopedLiveSession()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        var originalSession = host.Store.Session!.Id;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "different-connection",
            sessionNumber: 3));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        Assert.IsFalse(
            (await host.Service.GetCurrentSessionAsync(CancellationToken.None)).IsSuccess);

        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 3_000));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(originalSession, current.Value.Id);
        Assert.AreEqual(1, host.Store.EnsureSessionCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task ReconnectedReplayDoesNotReuseConnectionScopedLiveSession()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);

        await host.Telemetry.PublishAsync(TelemetryDisconnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.WaitingForSimulator);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsFalse(current.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.NoCurrentSession, current.Error!.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    public async Task EnteringUnmatchedReplayClearsPriorLiveCurrentSession()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            scope: SimulatorIdentityScope.Durable));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        Assert.IsTrue((await host.Service.GetCurrentSessionAsync(CancellationToken.None)).IsSuccess);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 2_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "unmatched-replay"));
        await WaitUntilAsync(() => host.Store.SessionLookupCount >= 2);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsFalse(current.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.NoCurrentSession, current.Error!.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task PersistentStoreFailurePublishesOneUnavailableTransitionWithoutFrameFlood()
    {
        var store = new StatefulStore { FailSessionLookup = true };
        await using var host = new TestHost(store);
        var collected = CollectUpdatesUntilErrorAsync(host.Service, SecondTestError.Code);
        await host.StartConnectedAsync();

        for (var index = 0; index < 3; index++)
        {
            await host.Telemetry.PublishAsync(CreateSample(
                counter: 0,
                time: 1_000 + index,
                scope: SimulatorIdentityScope.Durable));
        }

        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(SecondTestError));
        var updates = await collected;

        Assert.AreEqual(
            1,
            updates.OfType<ReviewUpdate.StatusChanged>().Count(update =>
                update.Error?.Code == TestError.Code));
        Assert.AreEqual(
            1,
            updates.OfType<ReviewUpdate.StatusChanged>().Count(update =>
                update.Status == ReviewServiceStatus.Connected));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task PreCancelledStartRemainsCancellationAndDoesNotStartRuntime()
    {
        await using var host = new TestHost();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => host.Runtime.StartAsync(cancellation.Token));

        Assert.AreEqual(
            ReviewServiceStatus.Stopped,
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value);
        Assert.IsFalse(host.Runtime.Completion.IsCompleted);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public async Task PublicCompositionSharesSingletonAndStopCompletesTelemetryLifetime()
    {
        await using var host = new TestHost();
        Assert.IsTrue(ReferenceEquals(host.Service, host.Runtime));
        await host.StartConnectedAsync();

        await host.StopAsync();

        Assert.IsTrue(host.Runtime.Completion.IsCompletedSuccessfully);
        Assert.AreEqual(
            ReviewServiceStatus.Stopped,
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value);
        await host.Telemetry.ObservationStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task StopBeforeStartIsTerminalAndRepeatedLifecycleCallsAreSafe()
    {
        await using var host = new TestHost();

        await host.Runtime.StopAsync(CancellationToken.None);
        await host.Runtime.StopAsync(CancellationToken.None);
        var start = await host.Runtime.StartAsync(CancellationToken.None);

        Assert.IsFalse(start.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.RuntimeStopped, start.Error!.Code);
        Assert.IsTrue(host.Runtime.Completion.IsCompletedSuccessfully);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    public async Task CommittedIndeterminateIncidentReconcilesWithoutDuplicateExecution()
    {
        var store = new StatefulStore
        {
            RecordBehavior = IndeterminateRecordBehavior.CommitThenReportIndeterminate,
        };
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 2_000));
        await WaitUntilAsync(() => store.OperationOutcomeQueryCount == 1);

        Assert.AreEqual(1, store.RecordAttemptCount);
        Assert.AreEqual(1, store.IncidentCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    public async Task NotCommittedIndeterminateIncidentRetriesTheExactCommand()
    {
        var store = new StatefulStore
        {
            RecordBehavior = IndeterminateRecordBehavior.RejectThenReportIndeterminate,
        };
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 2_000));
        await WaitUntilAsync(() => store.RecordAttemptCount == 2);

        Assert.AreEqual(1, store.OperationOutcomeQueryCount);
        Assert.AreEqual(1, store.IncidentCount);
        Assert.AreEqual(
            store.RecordAttempts[0].OperationId,
            store.RecordAttempts[1].OperationId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-STR-005")]
    public async Task TransientPersistenceFailureRetriesWithoutAnotherTelemetrySample()
    {
        var store = new StatefulStore { TransientRecordFailures = 1 };
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        await host.Telemetry.PublishAsync(CreateSample(counter: 2, time: 2_000));
        await WaitUntilAsync(() => store.IncidentCount == 1);

        Assert.AreEqual(2, store.RecordAttemptCount);
        Assert.AreEqual(
            store.RecordAttempts[0].OperationId,
            store.RecordAttempts[1].OperationId);
        Assert.AreEqual(
            2_000,
            store.RecordAttempts[1].Incident.Position.SessionTime.Milliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task AcceptedReplayCommandsClampLeadInWithoutCompletingReviewWorkflow()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 2_000);
        var store = new StatefulStore
        {
            Preferences = UserPreferences.TryCreateMilliseconds(
                replayLeadInMilliseconds: 3_000,
                playbackSpeed: 0.5,
                autoPause: false,
                preferredCamera: "TV1").Value,
        };
        SeedReviewIncident(store, incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, host.Replay.LastSeek!.SessionTime.Milliseconds);
        Assert.AreEqual(incident.Participant, host.Replay.LastFocusedParticipant);
        Assert.AreEqual("TV1", host.Replay.LastPreferredCamera);
        Assert.IsFalse(host.Replay.LastPlayback!.IsPaused);
        Assert.AreEqual(0.5, host.Replay.LastPlayback.PlaybackRate);
        CollectionAssert.AreEqual(
            SuccessfulReplayCommandOrder,
            host.Replay.DeliveredCommands.ToArray());
        Assert.IsNull(store.LastMarkedReviewed);
        Assert.AreEqual(IncidentReviewStatus.Pending, store.GetIncidentStatus(incident.Id));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [DataRow(1_000L, -2_000L, 0L)]
    [DataRow(5_000L, -2_000L, 3_000L)]
    [DataRow(5_000L, 0L, 5_000L)]
    [DataRow(5_000L, 2_000L, 7_000L)]
    public async Task ExplicitReplayOffsetTargetsTheRequestedMoment(
        long incidentMilliseconds,
        long offsetMilliseconds,
        long expectedMilliseconds)
    {
        var incident = CreateStoredIncident(incidentMilliseconds);
        var store = new StatefulStore
        {
            Preferences = UserPreferences.TryCreateMilliseconds(
                replayLeadInMilliseconds: 9_000,
                playbackSpeed: 0.5,
                autoPause: false,
                preferredCamera: "TV1").Value,
        };
        SeedReviewIncident(store, incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(
            incident.Id,
            ReplayOffset.TryCreateMilliseconds(offsetMilliseconds).Value,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(expectedMilliseconds, host.Replay.LastSeek!.SessionTime.Milliseconds);
        Assert.AreEqual("TV1", host.Replay.LastPreferredCamera);
        Assert.IsFalse(host.Replay.LastPlayback!.IsPaused);
        Assert.AreEqual(0.5, host.Replay.LastPlayback.PlaybackRate);
        CollectionAssert.AreEqual(
            SuccessfulReplayCommandOrder,
            host.Replay.DeliveredCommands.ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReviewRejectsAnIncidentFromADifferentSimulatorSession()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 2_000);
        var store = new StatefulStore();
        SeedReviewIncident(store, incident);
        var currentDescriptor = CreateSample(
            counter: 0,
            time: 1_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "current-session").Sample.Session;
        store.SeedSession(StoredSession.Create(
            SessionIdentity.Generate(),
            currentDescriptor,
            UtcInstant.TryCreateUnixMilliseconds(10_000).Value));
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "current-session"));
        await WaitUntilAsync(() => store.SessionLookupCount == 1);

        var result = await host.Service.ReviewIncidentAsync(
            incident.Id,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplaySessionNotLoaded, result.Error!.Code);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReviewRevalidatesOnTrackStateAfterStoreReads()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 2_000);
        var store = new StatefulStore { BlockPreferencesQuery = true };
        SeedReviewIncident(store, incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var review = host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);
        await store.PreferencesQueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_100,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.OnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        store.ReleasePreferencesQuery();

        var result = await review;

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplayDriverOnTrack, result.Error!.Code);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReviewRevalidatesSessionAfterStoreReads()
    {
        var store = new StatefulStore { BlockPreferencesQuery = true };
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 2_000));
        await WaitUntilAsync(() => store.RecordAttemptCount == 1);
        var incident = store.LastRecord!.Incident;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_100,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);

        var review = host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);
        await store.PreferencesQueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_200,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "different-connection",
            sessionNumber: 3));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        store.ReleasePreferencesQuery();

        var result = await review;

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplaySessionNotLoaded, result.Error!.Code);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReviewFailsClosedWhileNewLiveSessionIsResolving()
    {
        var store = new StatefulStore();
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 2_000));
        await WaitUntilAsync(() => store.RecordAttemptCount == 1);
        var incident = store.LastRecord!.Incident;
        store.BlockSessionLookup = true;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "new-live-session",
            sessionNumber: 3));
        await store.SessionLookupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var review = await host.Service.ReviewIncidentAsync(
            incident.Id,
            CancellationToken.None);

        Assert.IsFalse(review.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplaySessionNotLoaded, review.Error!.Code);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
        store.ReleaseSessionLookup();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task RejectedSeekStopsBeforeCameraAndPlayback()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        SeedReviewIncident(store, incident);
        var replay = new RecordingReplayController
        {
            SeekResult = Result.Failure(TestError),
        };
        await using var host = new TestHost(store, replay);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(1, replay.SeekCount);
        Assert.AreEqual(0, replay.FocusCount);
        Assert.AreEqual(0, replay.PlaybackCount);
        CollectionAssert.AreEqual(SeekOnlyCommandOrder, replay.DeliveredCommands.ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task RejectedCameraStopsBeforePlayback()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        SeedReviewIncident(store, incident);
        var replay = new RecordingReplayController
        {
            FocusResult = Result.Failure(TestError),
        };
        await using var host = new TestHost(store, replay);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(1, replay.SeekCount);
        Assert.AreEqual(1, replay.FocusCount);
        Assert.AreEqual(0, replay.PlaybackCount);
        CollectionAssert.AreEqual(
            SeekAndFocusCommandOrder,
            replay.DeliveredCommands.ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task RejectedPlaybackDoesNotMarkIncidentReviewed()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        SeedReviewIncident(store, incident);
        var replay = new RecordingReplayController
        {
            PlaybackResult = Result.Failure(TestError),
        };
        await using var host = new TestHost(store, replay);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(1, replay.FocusCount);
        Assert.IsNull(store.LastMarkedReviewed);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task UnsupportedPlaybackPreferenceFailsBeforeSeekOrDelivery()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore
        {
            Preferences = UserPreferences.TryCreateMilliseconds(1_000, 0.75, false, null).Value,
        };
        SeedReviewIncident(store, incident);
        var replay = new RecordingReplayController { ValidationResult = Result.Failure(TestError) };
        await using var host = new TestHost(store, replay);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(1, replay.ValidationCount);
        Assert.AreEqual(0, replay.SeekCount);
        Assert.AreEqual(0, replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task UnsupportedPlayingPreferenceIsNotPersisted()
    {
        var store = new StatefulStore();
        var replay = new RecordingReplayController { ValidationResult = Result.Failure(TestError) };
        await using var host = new TestHost(store, replay);

        var result = await host.Service.UpdatePreferencesAsync(
            UserPreferences.TryCreateMilliseconds(1_000, 0.75, false, null).Value,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(1, replay.ValidationCount);
        Assert.AreEqual(0, store.PreferencesExecuteCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task SubMillisecondSystemTimeIsNormalizedAtTheApplicationBoundary()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        var preciseNow = DateTimeOffset.FromUnixTimeMilliseconds(20_000).AddTicks(1_234);
        await using var host = new TestHost(
            store,
            timeProvider: new FixedTimeProvider(preciseNow));

        var preferences = await host.Service.UpdatePreferencesAsync(
            UserPreferences.TryCreateMilliseconds(1_000, 1, true, null).Value,
            CancellationToken.None);
        var annotation = await host.Service.AnnotateIncidentAsync(
            incident.Id,
            IncidentAnnotation.TryCreate("note", IncidentClassification.Contact).Value,
            CancellationToken.None);

        Assert.IsTrue(preferences.IsSuccess);
        Assert.IsTrue(annotation.IsSuccess);
        Assert.AreEqual(
            preciseNow.ToUnixTimeMilliseconds(),
            store.LastPreferencesUpdate!.UpdatedAt.UnixMilliseconds);
        Assert.AreEqual(
            preciseNow.ToUnixTimeMilliseconds(),
            store.LastAnnotation!.UpdatedAt.UnixMilliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task OnTrackStateFailsClosedBeforeAnyReplayCommand()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            onTrackState: OnTrackState.OnTrack));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplayDriverOnTrack, result.Error!.Code);
        Assert.AreEqual(ApplicationErrors.ReplayDriverOnTrack.Message, result.Error.Message);
        Assert.IsNull(host.Replay.LastSeek);
        Assert.IsNull(store.LastMarkedReviewed);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task UnknownOnTrackStateReportsThatTelemetryHasNotArrived()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplayOnTrackStateUnknown, result.Error!.Code);
        Assert.AreEqual(ApplicationErrors.ReplayOnTrackStateUnknown.Message, result.Error.Message);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task DisconnectedTelemetryReportsReplayUnavailable()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(TelemetryDisconnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.WaitingForSimulator);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplayUnavailable, result.Error!.Code);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, result.Error.Message);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task StoppedRuntimeReportsRuntimeStopped()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        await using var host = new TestHost(store);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.RuntimeStopped, result.Error!.Code);
        Assert.AreEqual(ApplicationErrors.RuntimeStopped.Message, result.Error.Message);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task UnavailableRuntimePreservesItsSpecificFailure()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        store.SeedIncident(incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var result = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(TestError, result.Error);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ConcurrentReviewReportsCommandAlreadyInProgress()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore { BlockPreferencesQuery = true };
        SeedReviewIncident(store, incident);
        await using var host = new TestHost(store);
        await host.StartConnectedAsync();
        await MakeReplayAvailableAsync(host);

        var firstReview = host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);
        await store.PreferencesQueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondReview = await host.Service.ReviewIncidentAsync(
            incident.Id,
            CancellationToken.None);

        Assert.IsFalse(secondReview.IsSuccess);
        Assert.AreEqual(ApplicationErrorCodes.ReplayCommandInProgress, secondReview.Error!.Code);
        Assert.AreEqual(ApplicationErrors.ReplayCommandInProgress.Message, secondReview.Error.Message);
        Assert.AreEqual(0, host.Replay.SeekCount);
        Assert.AreEqual(0, host.Replay.PlaybackCount);

        store.ReleasePreferencesQuery();
        Assert.IsTrue((await firstReview).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task OwnedReplaySuspendsDetectionUntilAConfirmedOnTrackSample()
    {
        var incident = CreateStoredIncident(positionMilliseconds: 5_000);
        var store = new StatefulStore();
        SeedReviewIncident(store, incident);
        var replay = new RecordingReplayController();
        await using var host = new TestHost(store, replay);
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => store.BaselineCount == 1);

        var review = await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None);
        Assert.IsTrue(review.IsSuccess);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_000,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        Assert.AreEqual(0, store.RecordAttemptCount);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_500,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(SecondTestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        Assert.AreEqual(0, store.RecordAttemptCount);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 3_000,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.OnTrack));
        await WaitUntilAsync(() => store.RecordAttemptCount == 1);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "IR-SES-001")]
    public async Task OwnedReviewStillProcessesReplaySessionTransitions()
    {
        await using var host = new TestHost();
        await host.StartConnectedAsync();
        await host.Telemetry.PublishAsync(CreateSample(counter: 0, time: 1_000));
        await WaitUntilAsync(() => host.Store.BaselineCount == 1);
        await host.Telemetry.PublishAsync(CreateSample(counter: 4, time: 2_000));
        await WaitUntilAsync(() => host.Store.RecordAttemptCount == 1);
        var incident = host.Store.LastRecord!.Incident;

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_100,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);
        Assert.IsTrue(
            (await host.Service.ReviewIncidentAsync(incident.Id, CancellationToken.None)).IsSuccess);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_200,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack,
            sessionKey: "different-connection",
            sessionNumber: 3));
        await WaitUntilAsync(async () =>
            !(await host.Service.GetCurrentSessionAsync(CancellationToken.None)).IsSuccess);

        await host.Telemetry.PublishAsync(CreateSample(
            counter: 4,
            time: 2_300,
            mode: SessionMode.Replay,
            onTrackState: OnTrackState.NotOnTrack));
        await host.Telemetry.PublishAsync(TelemetryUnavailable.Create(TestError));
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Unavailable);

        var current = await host.Service.GetCurrentSessionAsync(CancellationToken.None);
        Assert.IsTrue(current.IsSuccess);
        Assert.AreEqual(incident.Session, current.Value.Id);
        await host.Telemetry.PublishAsync(TelemetryConnected.Instance);
        await WaitUntilAsync(async () =>
            (await host.Service.GetStatusAsync(CancellationToken.None)).Value ==
            ReviewServiceStatus.Connected);

        var secondReview = await host.Service.ReviewIncidentAsync(
            incident.Id,
            CancellationToken.None);

        Assert.IsTrue(secondReview.IsSuccess);
        Assert.AreEqual(2, host.Replay.SeekCount);
        Assert.AreEqual(2, host.Replay.PlaybackCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task FailedReconciliationLookupPreservesIndeterminateCommit()
    {
        var store = new StatefulStore
        {
            ReportPreferencesIndeterminate = true,
            FailOperationOutcomeQuery = true,
        };
        await using var host = new TestHost(store);

        var result = await host.Service.UpdatePreferencesAsync(
            UserPreferences.TryCreateMilliseconds(1_000, 0.5, true, null).Value,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.IndeterminateCommit, result.Error!.Code);
        Assert.AreEqual(1, store.PreferencesExecuteCount);
        Assert.AreEqual(1, store.OperationOutcomeQueryCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CallerCancellationAfterIndeterminateCommitPreservesIndeterminateResult()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new StatefulStore
        {
            ReportPreferencesIndeterminate = true,
            CancelAfterPreferencesIndeterminate = cancellation,
        };
        await using var host = new TestHost(store);

        var result = await host.Service.UpdatePreferencesAsync(
            UserPreferences.TryCreateMilliseconds(1_000, 0.5, true, null).Value,
            cancellation.Token);

        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.IndeterminateCommit, result.Error!.Code);
        Assert.AreEqual(1, store.PreferencesExecuteCount);
        Assert.AreEqual(0, store.OperationOutcomeQueryCount);
    }

    private static readonly Error TestError = Error.Create(
        ErrorCode.Define("test.replay.rejected"),
        ErrorKind.Unavailable,
        "The test replay command was rejected.");
    private static readonly Error SecondTestError = Error.Create(
        ErrorCode.Define("test.telemetry.second-failure"),
        ErrorKind.Unavailable,
        "The second test telemetry failure.");

    private static async Task MakeReplayAvailableAsync(TestHost host)
    {
        var previousLookups = host.Store.SessionLookupCount;
        await host.Telemetry.PublishAsync(CreateSample(
            counter: 0,
            time: 1_000,
            mode: SessionMode.Replay,
            scope: SimulatorIdentityScope.Durable,
            onTrackState: OnTrackState.NotOnTrack));
        await WaitUntilAsync(() => host.Store.SessionLookupCount > previousLookups);
    }

    private static async Task<IReadOnlyList<ReviewUpdate>> CollectUpdatesUntilErrorAsync(
        IIncidentReviewService service,
        ErrorCode terminalError)
    {
        var updates = new List<ReviewUpdate>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var update in service.ObserveUpdatesAsync(timeout.Token))
        {
            updates.Add(update);
            if (update is ReviewUpdate.StatusChanged status &&
                status.Error?.Code == terminalError)
            {
                return updates.AsReadOnly();
            }
        }

        throw new AssertFailedException("The terminal update was not observed.");
    }

    private static TelemetrySampleObserved CreateSample(
        int counter,
        long time,
        SessionMode? mode = null,
        SimulatorIdentityScope? scope = null,
        OnTrackState onTrackState = OnTrackState.OnTrack,
        string sessionKey = "test-session",
        int sessionNumber = 2,
        IReadOnlyList<ParticipantIncidentCounter>? incidentCounters = null)
    {
        var validatedSessionNumber = SessionNumber.TryCreate(sessionNumber).Value;
        var descriptor = SimulatorSessionDescriptor.TryCreate(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate(sessionKey).Value,
            validatedSessionNumber,
            mode ?? SessionMode.Live,
            scope ?? SimulatorIdentityScope.ConnectionScoped).Value;
        var position = ReplayPosition.TryCreate(
            validatedSessionNumber,
            SessionTime.TryCreateMilliseconds(time).Value).Value;
        var sample = TelemetrySample.TryCreate(
            descriptor,
            position,
            incidentCounters ??
            [
                CreateParticipantCounter(
                    DefaultParticipant,
                    counter,
                    lap: 1,
                    lapDistance: 0.5),
            ],
            onTrackState,
            UtcInstant.TryCreateUnixMilliseconds(10_000 + time).Value).Value;
        return TelemetrySampleObserved.Create(sample);
    }

    private static ParticipantIncidentCounter CreateParticipantCounter(
        IncidentParticipant participant,
        int counter,
        int? lap = null,
        double? lapDistance = null) => ParticipantIncidentCounter.TryCreate(
            participant,
            IncidentCounter.TryCreate(counter).Value,
            lap.HasValue ? LapNumber.TryCreate(lap.Value).Value : null,
            lapDistance.HasValue ? LapDistance.TryCreate(lapDistance.Value).Value : null).Value;

    private static IncidentParticipant CreateParticipant(
        string identity,
        string? driverName,
        string? teamName,
        string? carNumber) => IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate(identity).Value,
            driverName,
            teamName,
            carNumber).Value;

    private static StoredIncident CreateStoredIncident(long positionMilliseconds)
    {
        var createdAt = UtcInstant.TryCreateUnixMilliseconds(10_000).Value;
        return StoredIncident.Create(
            IncidentId.Generate(),
            SessionIdentity.Generate(),
            DefaultParticipant,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(2).Value,
                SessionTime.TryCreateMilliseconds(positionMilliseconds).Value).Value,
            createdAt,
            IncidentPoints.TryCreate(total: 4, delta: 4).Value,
            CounterEpoch.TryCreate(0).Value,
            LapNumber.TryCreate(1).Value,
            LapDistance.TryCreate(0.5).Value,
            IncidentReviewStatus.Pending,
            IncidentAnnotation.TryCreate(null, null).Value,
            createdAt,
            createdAt);
    }

    private static void SeedReviewIncident(StatefulStore store, StoredIncident incident)
    {
        store.SeedIncident(incident);
        store.SeedSession(StoredSession.Create(
            incident.Session,
            CreateSample(
                counter: 0,
                time: 1_000,
                mode: SessionMode.Live,
                scope: SimulatorIdentityScope.Durable).Sample.Session,
            incident.CreatedAt));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private bool _started;

        public TestHost(
            StatefulStore? store = null,
            RecordingReplayController? replay = null,
            RecordingReplayContextReader? replayContext = null,
            TimeProvider? timeProvider = null)
        {
            Store = store ?? new StatefulStore();
            Replay = replay ?? new RecordingReplayController();
            ReplayContext = replayContext ?? new RecordingReplayContextReader();
            Telemetry = new ControllableTelemetrySource();
            var services = new ServiceCollection();
            _ = services.AddSingleton<ITelemetrySource>(Telemetry);
            _ = services.AddSingleton<IReplayController>(Replay);
            _ = services.AddSingleton<IReplayContextReader>(ReplayContext);
            _ = services.AddSingleton<IStore>(Store);
            _ = services.AddSingleton(timeProvider ?? new FixedTimeProvider(FixedNow));
            _ = services.AddIncidentReviewApplication();
            _provider = services.BuildServiceProvider();
            Runtime = _provider.GetRequiredService<IApplicationRuntime>();
            Service = _provider.GetRequiredService<IIncidentReviewService>();
        }

        public static DateTimeOffset FixedNow { get; } =
            DateTimeOffset.FromUnixTimeMilliseconds(20_000);
        public StatefulStore Store { get; }
        public RecordingReplayController Replay { get; }
        public RecordingReplayContextReader ReplayContext { get; }
        public ControllableTelemetrySource Telemetry { get; }
        public IApplicationRuntime Runtime { get; }
        public IIncidentReviewService Service { get; }

        public async Task StartConnectedAsync()
        {
            Assert.IsTrue((await Runtime.StartAsync(CancellationToken.None)).IsSuccess);
            _started = true;
            await Telemetry.PublishAsync(TelemetryConnected.Instance);
            await WaitUntilAsync(async () =>
                (await Service.GetStatusAsync(CancellationToken.None)).Value ==
                ReviewServiceStatus.Connected);
        }

        public async Task StopAsync()
        {
            await Runtime.StopAsync(CancellationToken.None);
            _started = false;
        }

        public async ValueTask DisposeAsync()
        {
            if (_started)
            {
                await StopAsync();
            }

            await _provider.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ControllableTelemetrySource : ITelemetrySource
    {
        private readonly Channel<TelemetryEvent> _events = Channel.CreateUnbounded<TelemetryEvent>();

        public TaskCompletionSource ObservationStopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<TelemetryEvent> ObserveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var telemetryEvent in _events.Reader
                                   .ReadAllAsync(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    yield return telemetryEvent;
                }
            }
            finally
            {
                ObservationStopped.TrySetResult();
            }
        }

        public ValueTask PublishAsync(TelemetryEvent telemetryEvent) =>
            _events.Writer.WriteAsync(telemetryEvent);
    }

    private sealed class RecordingReplayController : IReplayController
    {
        private readonly List<string> _deliveredCommands = [];

        public Result SeekResult { get; init; } = Result.Success();
        public Result FocusResult { get; init; } = Result.Success();
        public Result PlaybackResult { get; init; } = Result.Success();
        public Result ValidationResult { get; init; } = Result.Success();
        public ReplayPosition? LastSeek { get; private set; }
        public IncidentParticipant? LastFocusedParticipant { get; private set; }
        public string? LastPreferredCamera { get; private set; }
        public ReplayPlayback? LastPlayback { get; private set; }
        public IReadOnlyList<string> DeliveredCommands => _deliveredCommands;
        public int ValidationCount { get; private set; }
        public int SeekCount { get; private set; }
        public int FocusCount { get; private set; }
        public int PlaybackCount { get; private set; }

        public Result ValidatePlayback(ReplayPlayback playback)
        {
            ArgumentNullException.ThrowIfNull(playback);
            ValidationCount++;
            return ValidationResult;
        }

        public ValueTask<Result> SeekAsync(
            ReplayPosition position,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeekCount++;
            LastSeek = position;
            _deliveredCommands.Add("seek");
            return ValueTask.FromResult(SeekResult);
        }

        public ValueTask<Result> FocusParticipantAsync(
            IncidentParticipant participant,
            string? preferredCamera,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(participant);
            cancellationToken.ThrowIfCancellationRequested();
            FocusCount++;
            LastFocusedParticipant = participant;
            LastPreferredCamera = preferredCamera;
            _deliveredCommands.Add("focus-player");
            return ValueTask.FromResult(FocusResult);
        }

        public ValueTask<Result> SetPlaybackAsync(
            ReplayPlayback playback,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlaybackCount++;
            LastPlayback = playback;
            _deliveredCommands.Add("set-playback");
            return ValueTask.FromResult(PlaybackResult);
        }
    }

    private sealed class RecordingReplayContextReader : IReplayContextReader
    {
        public Result<ReplayContext> ContextResult { get; init; } =
            Result<ReplayContext>.Success(ReplayContext.TryCreate(null, [], null).Value);

        public int ReadCount { get; private set; }

        public Result<ReplayContext> Read()
        {
            ReadCount++;
            return ContextResult;
        }
    }

    private sealed class StatefulStore : IStore
    {
        private readonly object _lock = new();
        private readonly HashSet<OperationId> _committedOperations = [];
        private readonly List<StoredIncident> _incidents = [];
        private readonly List<EstablishIncidentCheckpoint> _baselines = [];
        private readonly List<RecordDetectedIncident> _recordAttempts = [];
        private readonly Dictionary<ParticipantIdentity, IncidentCheckpoint> _checkpoints = [];
        private int _ensureSessionCount;
        private int _checkpointQueryCount;
        private int _sessionLookupCount;
        private int _operationOutcomeQueryCount;
        private int _preferencesExecuteCount;
        private UserPreferences _preferences = UserPreferences.TryCreateMilliseconds(
            replayLeadInMilliseconds: 3_000,
            playbackSpeed: 1,
            autoPause: true,
            preferredCamera: null).Value;

        public UserPreferences Preferences
        {
            get => _preferences;
            init => _preferences = value;
        }
        public IndeterminateRecordBehavior RecordBehavior { get; init; }
        public int TransientRecordFailures { get; init; }
        public bool ReportPreferencesIndeterminate { get; init; }
        public CancellationTokenSource? CancelAfterPreferencesIndeterminate { get; init; }
        public bool FailOperationOutcomeQuery { get; init; }
        public bool FailSessionLookup { get; init; }
        public bool BlockPreferencesQuery { get; init; }
        public bool BlockSessionLookup { get; set; }
        public TaskCompletionSource PreferencesQueryEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SessionLookupEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource ContinuePreferencesQuery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource ContinueSessionLookup { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StoredSession? Session { get; private set; }
        public IncidentCheckpoint? Checkpoint
        {
            get
            {
                lock (_lock)
                {
                    return _checkpoints.GetValueOrDefault(DefaultParticipantIdentity);
                }
            }
        }
        public MarkIncidentReviewed? LastMarkedReviewed { get; private set; }
        public AnnotateIncident? LastAnnotation { get; private set; }
        public UpdatePreferences? LastPreferencesUpdate { get; private set; }
        public int EnsureSessionCount => Volatile.Read(ref _ensureSessionCount);
        public int CheckpointQueryCount => Volatile.Read(ref _checkpointQueryCount);
        public int SessionLookupCount => Volatile.Read(ref _sessionLookupCount);
        public int OperationOutcomeQueryCount => Volatile.Read(ref _operationOutcomeQueryCount);
        public int PreferencesExecuteCount => Volatile.Read(ref _preferencesExecuteCount);

        public IReadOnlyList<EstablishIncidentCheckpoint> Baselines
        {
            get
            {
                lock (_lock)
                {
                    return _baselines.ToArray();
                }
            }
        }

        public int BaselineCount
        {
            get
            {
                lock (_lock)
                {
                    return _baselines.Count;
                }
            }
        }

        public EstablishIncidentCheckpoint? LastBaseline
        {
            get
            {
                lock (_lock)
                {
                    return _baselines.LastOrDefault();
                }
            }
        }

        public RecordDetectedIncident[] RecordAttempts
        {
            get
            {
                lock (_lock)
                {
                    return _recordAttempts.ToArray();
                }
            }
        }

        public int RecordAttemptCount
        {
            get
            {
                lock (_lock)
                {
                    return _recordAttempts.Count;
                }
            }
        }

        public RecordDetectedIncident? LastRecord
        {
            get
            {
                lock (_lock)
                {
                    return _recordAttempts.LastOrDefault();
                }
            }
        }

        public int IncidentCount
        {
            get
            {
                lock (_lock)
                {
                    return _incidents.Count;
                }
            }
        }

        public void SeedIncident(StoredIncident incident)
        {
            lock (_lock)
            {
                _incidents.Add(incident);
            }
        }

        public void SeedSession(StoredSession session)
        {
            lock (_lock)
            {
                Session = session;
            }
        }

        public IncidentReviewStatus? GetIncidentStatus(IncidentId incident)
        {
            lock (_lock)
            {
                return _incidents.SingleOrDefault(item => item.Id == incident)?.ReviewStatus;
            }
        }

        public async Task<Result<T>> QueryAsync<T>(
            IStoreQuery<T> query,
            CancellationToken cancellationToken)
            where T : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query is GetPreferences && BlockPreferencesQuery)
            {
                PreferencesQueryEntered.TrySetResult();
                await ContinuePreferencesQuery.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (query is GetSessionBySimulatorKey && BlockSessionLookup)
            {
                SessionLookupEntered.TrySetResult();
                await ContinueSessionLookup.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_lock)
            {
                object result = query switch
                {
                    GetPreferences => Result<UserPreferences>.Success(Preferences),
                    GetOperationOutcome operation => QueryOperation(operation),
                    GetSessionBySimulatorKey byKey => QuerySession(byKey),
                    GetSession byId => Result<StoreLookup<StoredSession>>.Success(
                        Session?.Id == byId.Session
                            ? StoreLookup.Found(Session)
                            : StoreLookup.Missing<StoredSession>()),
                    GetSessionDetails details => QuerySessionDetails(details),
                    GetIncident incident => Result<StoreLookup<StoredIncident>>.Success(
                        LookupIncident(incident.Incident)),
                    GetIncidents incidents => Result<IReadOnlyList<StoredIncident>>.Success(
                        Array.AsReadOnly(_incidents
                            .Where(item => item.Session == incidents.Session)
                            .ToArray())),
                    GetIncidentCheckpoints checkpoints => QueryCheckpoints(checkpoints),
                    ListSessions => QuerySessions(),
                    _ => Result<T>.Failure(TestError),
                };
                return (Result<T>)result;
            }
        }

        public void ReleasePreferencesQuery() => ContinuePreferencesQuery.TrySetResult();

        public void ReleaseSessionLookup() => ContinueSessionLookup.TrySetResult();

        public Task<Result> ExecuteAsync(
            IStoreCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_committedOperations.Contains(command.OperationId))
                {
                    return Task.FromResult(Result.Success());
                }

                var result = command switch
                {
                    EnsureSession ensure => EnsureSession(ensure),
                    EstablishIncidentCheckpoint baseline => EstablishBaseline(baseline),
                    RecordDetectedIncident record => RecordIncident(record),
                    MarkIncidentReviewed reviewed => MarkReviewed(reviewed),
                    AnnotateIncident annotate => Annotate(annotate),
                    UpdatePreferences update => UpdatePreferences(update),
                    _ => Result.Failure(TestError),
                };
                return Task.FromResult(result);
            }
        }

        private Result<OperationOutcome> QueryOperation(GetOperationOutcome query)
        {
            Interlocked.Increment(ref _operationOutcomeQueryCount);
            if (FailOperationOutcomeQuery)
            {
                return Result<OperationOutcome>.Failure(TestError);
            }

            return Result<OperationOutcome>.Success(
                _committedOperations.Contains(query.OperationId)
                    ? OperationOutcome.Committed
                    : OperationOutcome.NotCommitted);
        }

        private Result<StoreLookup<StoredSession>> QuerySession(GetSessionBySimulatorKey query)
        {
            Interlocked.Increment(ref _sessionLookupCount);
            if (FailSessionLookup)
            {
                return Result<StoreLookup<StoredSession>>.Failure(TestError);
            }

            var found = Session is not null &&
                        Session.Descriptor.Simulator == query.Simulator &&
                        Session.Descriptor.SessionKey == query.SessionKey;
            return Result<StoreLookup<StoredSession>>.Success(
                found ? StoreLookup.Found(Session!) : StoreLookup.Missing<StoredSession>());
        }

        private Result<StoreLookup<StoredSessionDetails>> QuerySessionDetails(GetSessionDetails query)
        {
            if (Session?.Id != query.Session)
            {
                return Result<StoreLookup<StoredSessionDetails>>.Success(
                    StoreLookup.Missing<StoredSessionDetails>());
            }

            return Result<StoreLookup<StoredSessionDetails>>.Success(
                StoreLookup.Found(StoredSessionDetails.Create(
                    Session,
                    _incidents.Where(item => item.Session == query.Session))));
        }

        private Result<IReadOnlyList<IncidentCheckpoint>> QueryCheckpoints(
            GetIncidentCheckpoints query)
        {
            Interlocked.Increment(ref _checkpointQueryCount);
            IReadOnlyList<IncidentCheckpoint> checkpoints = Array.AsReadOnly(
                _checkpoints.Values
                    .Where(checkpoint => checkpoint.Session == query.Session)
                    .OrderBy(checkpoint => checkpoint.ParticipantIdentity.Value, StringComparer.Ordinal)
                    .ToArray());
            return Result<IReadOnlyList<IncidentCheckpoint>>.Success(checkpoints);
        }

        private Result<IReadOnlyList<StoredSessionSummary>> QuerySessions()
        {
            IReadOnlyList<StoredSessionSummary> sessions = Session is null
                ? Array.Empty<StoredSessionSummary>()
                : Array.AsReadOnly([
                    StoredSessionSummary.Create(
                        Session,
                        _incidents.Count(item => item.Session == Session.Id),
                        _incidents.Count(item =>
                            item.Session == Session.Id &&
                            item.ReviewStatus == IncidentReviewStatus.Pending)),
                ]);
            return Result<IReadOnlyList<StoredSessionSummary>>.Success(sessions);
        }

        private StoreLookup<StoredIncident> LookupIncident(IncidentId incident)
        {
            var found = _incidents.SingleOrDefault(item => item.Id == incident);
            return found is null
                ? StoreLookup.Missing<StoredIncident>()
                : StoreLookup.Found(found);
        }

        private Result EnsureSession(EnsureSession command)
        {
            Interlocked.Increment(ref _ensureSessionCount);
            if (Session is not null)
            {
                return Result.Failure(StoreErrors.SessionIdentityConflict);
            }

            Session = StoredSession.Create(
                command.ProposedIdentity,
                command.Descriptor,
                command.StartedAt);
            _committedOperations.Add(command.OperationId);
            return Result.Success();
        }

        private Result EstablishBaseline(EstablishIncidentCheckpoint command)
        {
            _baselines.Add(command);
            _ = _checkpoints.TryGetValue(
                command.NextCheckpoint.ParticipantIdentity,
                out var currentCheckpoint);
            if (currentCheckpoint != command.ExpectedCheckpoint)
            {
                return Result.Failure(StoreErrors.CheckpointConflict);
            }

            _checkpoints[command.NextCheckpoint.ParticipantIdentity] = command.NextCheckpoint;
            _committedOperations.Add(command.OperationId);
            return Result.Success();
        }

        private Result RecordIncident(RecordDetectedIncident command)
        {
            _recordAttempts.Add(command);
            if (_recordAttempts.Count <= TransientRecordFailures)
            {
                return Result.Failure(StoreErrors.PersistenceFailure);
            }

            if (_recordAttempts.Count == 1 &&
                RecordBehavior == IndeterminateRecordBehavior.RejectThenReportIndeterminate)
            {
                return Result.Failure(StoreErrors.IndeterminateCommit);
            }

            var applied = ApplyIncident(command);
            if (!applied.IsSuccess)
            {
                return applied;
            }

            if (_recordAttempts.Count == 1 &&
                RecordBehavior == IndeterminateRecordBehavior.CommitThenReportIndeterminate)
            {
                return Result.Failure(StoreErrors.IndeterminateCommit);
            }

            return Result.Success();
        }

        private Result ApplyIncident(RecordDetectedIncident command)
        {
            _ = _checkpoints.TryGetValue(
                command.NextCheckpoint.ParticipantIdentity,
                out var currentCheckpoint);
            if (currentCheckpoint != command.ExpectedCheckpoint)
            {
                return Result.Failure(StoreErrors.CheckpointConflict);
            }

            _incidents.Add(command.Incident);
            _checkpoints[command.NextCheckpoint.ParticipantIdentity] = command.NextCheckpoint;
            _committedOperations.Add(command.OperationId);
            return Result.Success();
        }

        private Result MarkReviewed(MarkIncidentReviewed command)
        {
            var index = _incidents.FindIndex(item => item.Id == command.Incident);
            if (index < 0)
            {
                return Result.Failure(StoreErrors.EntityNotFound);
            }

            var incident = _incidents[index];
            _incidents[index] = StoredIncident.Create(
                incident.Id,
                incident.Session,
                incident.Participant,
                incident.Position,
                incident.ObservedAt,
                incident.Points,
                incident.CounterEpoch,
                incident.Lap,
                incident.LapDistance,
                IncidentReviewStatus.Reviewed,
                incident.Annotation,
                incident.CreatedAt,
                command.ReviewedAt);
            LastMarkedReviewed = command;
            _committedOperations.Add(command.OperationId);
            return Result.Success();
        }

        private Result UpdatePreferences(UpdatePreferences command)
        {
            LastPreferencesUpdate = command;
            Interlocked.Increment(ref _preferencesExecuteCount);
            if (!ReportPreferencesIndeterminate)
            {
                _preferences = command.Preferences;
                _committedOperations.Add(command.OperationId);
                return Result.Success();
            }

            CancelAfterPreferencesIndeterminate?.Cancel();
            return Result.Failure(StoreErrors.IndeterminateCommit);
        }

        private Result Annotate(AnnotateIncident command)
        {
            LastAnnotation = command;
            _committedOperations.Add(command.OperationId);
            return Result.Success();
        }
    }

    private enum IndeterminateRecordBehavior
    {
        None,
        CommitThenReportIndeterminate,
        RejectThenReportIndeterminate,
    }
}
