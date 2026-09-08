using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Testing;

namespace IncidentReview.Store.Sqlite.Tests;

[TestClass]
public sealed class SqliteIncidentStoreTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-HIS-001")]
    public async Task EnsureSessionRoundTripsTypedLookupsAndNewestFirstHistory()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var first = EnsureSession.Create(
            SessionIdentity.Generate(),
            CreateDescriptor("session-one"),
            UtcInstant.TryCreateUnixMilliseconds(1_000).Value);
        var second = EnsureSession.Create(
            SessionIdentity.Generate(),
            CreateDescriptor("session-two"),
            UtcInstant.TryCreateUnixMilliseconds(2_000).Value);

        Assert.IsTrue((await context.Store.ExecuteAsync(first, CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(second, CancellationToken.None)).IsSuccess);

        var byIdentity = await context.Store.QueryAsync(
            new GetSession(first.ProposedIdentity),
            CancellationToken.None);
        var bySimulatorKey = await context.Store.QueryAsync(
            new GetSessionBySimulatorKey(
                second.Descriptor.Simulator,
                second.Descriptor.SessionKey),
            CancellationToken.None);
        var history = await context.Store.QueryAsync(ListSessions.Instance, CancellationToken.None);

        Assert.IsTrue(byIdentity.IsSuccess);
        Assert.AreEqual(first.ProposedIdentity, byIdentity.Value.Value!.Id);
        Assert.IsTrue(bySimulatorKey.IsSuccess);
        Assert.AreEqual(second.ProposedIdentity, bySimulatorKey.Value.Value!.Id);
        Assert.IsTrue(history.IsSuccess);
        Assert.HasCount(2, history.Value);
        Assert.AreEqual(second.ProposedIdentity, history.Value[0].Session.Id);
        Assert.AreEqual(first.ProposedIdentity, history.Value[1].Session.Id);

        var duplicateKey = EnsureSession.Create(
            SessionIdentity.Generate(),
            second.Descriptor,
            UtcInstant.TryCreateUnixMilliseconds(3_000).Value);
        AssertFailure(
            await context.Store.ExecuteAsync(duplicateKey, CancellationToken.None),
            StoreErrorCodes.SessionIdentityConflict,
            ErrorKind.Conflict);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-STR-005")]
    public async Task CheckpointCompareAndSwapKeepsIncidentInsertAtomicAndRetryIdempotent()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "atomic-session");
        var baseline = CreateCheckpoint(session, counter: 0, time: 1_000);
        var establish = EstablishIncidentCheckpoint.TryCreate(null, baseline).Value;
        Assert.IsTrue((await context.Store.ExecuteAsync(establish, CancellationToken.None)).IsSuccess);

        var staleBaseline = EstablishIncidentCheckpoint.TryCreate(
            expectedCheckpoint: null,
            CreateCheckpoint(session, counter: 1, time: 1_500)).Value;
        AssertFailure(
            await context.Store.ExecuteAsync(staleBaseline, CancellationToken.None),
            StoreErrorCodes.CheckpointConflict,
            ErrorKind.Conflict);

        var next = CreateCheckpoint(session, counter: 2, time: 2_000);
        var incident = CreateIncident(session, total: 2, delta: 2, time: 2_000);
        var record = RecordDetectedIncident.TryCreate(incident, baseline, next).Value;
        Assert.IsTrue((await context.Store.ExecuteAsync(record, CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(record, CancellationToken.None)).IsSuccess);

        var conflictingIncident = CreateIncident(session, total: 3, delta: 3, time: 3_000);
        var conflicting = RecordDetectedIncident.TryCreate(
            conflictingIncident,
            baseline,
            CreateCheckpoint(session, counter: 3, time: 3_000)).Value;
        AssertFailure(
            await context.Store.ExecuteAsync(conflicting, CancellationToken.None),
            StoreErrorCodes.CheckpointConflict,
            ErrorKind.Conflict);

        var incidents = await context.Store.QueryAsync(new GetIncidents(session), CancellationToken.None);
        var checkpoint = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);
        var retryOutcome = await context.Store.QueryAsync(
            new GetOperationOutcome(record.OperationId),
            CancellationToken.None);
        var conflictOutcome = await context.Store.QueryAsync(
            new GetOperationOutcome(conflicting.OperationId),
            CancellationToken.None);
        Assert.IsTrue(incidents.IsSuccess);
        Assert.HasCount(1, incidents.Value);
        Assert.AreEqual(incident.Id, incidents.Value[0].Id);
        Assert.HasCount(1, checkpoint.Value);
        Assert.AreEqual(next, checkpoint.Value[0]);
        Assert.AreSame(OperationOutcome.Committed, retryOutcome.Value);
        Assert.AreSame(OperationOutcome.NotCommitted, conflictOutcome.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    [TestProperty("Requirement", "IR-STR-005")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CancellationAfterIncidentInsertRollsBackTheEntireRecordCommand()
    {
        using var database = new TemporarySqliteDatabase();
        using var cancellation = new CancellationTokenSource();
        await using var context = SqliteTestingRegistration.CreateStoreCancellingAfterIncidentInsert(
            database.Options,
            cancellation);
        Assert.IsTrue((await context.Initializer.InitializeAsync(CancellationToken.None)).IsSuccess);
        var session = await EnsureSessionAsync(context.Store, "cancel-record-session");
        var baseline = CreateCheckpoint(session, counter: 0, time: 1_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, baseline).Value,
            CancellationToken.None)).IsSuccess);
        var incident = CreateIncident(session, total: 2, delta: 2, time: 2_000);
        var next = CreateCheckpoint(session, counter: 2, time: 2_000);
        var command = RecordDetectedIncident.TryCreate(incident, baseline, next).Value;

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            context.Store.ExecuteAsync(command, cancellation.Token));

        Assert.IsTrue(cancellation.IsCancellationRequested);
        var incidentsAfterCancellation = await context.Store.QueryAsync(
            new GetIncidents(session),
            CancellationToken.None);
        var checkpointAfterCancellation = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);
        var outcomeAfterCancellation = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);
        Assert.IsTrue(incidentsAfterCancellation.IsSuccess);
        Assert.IsEmpty(incidentsAfterCancellation.Value);
        Assert.HasCount(1, checkpointAfterCancellation.Value);
        Assert.AreEqual(baseline, checkpointAfterCancellation.Value[0]);
        Assert.AreSame(OperationOutcome.NotCommitted, outcomeAfterCancellation.Value);

        Assert.IsTrue((await context.Store.ExecuteAsync(command, CancellationToken.None)).IsSuccess);
        var incidentsAfterRetry = await context.Store.QueryAsync(
            new GetIncidents(session),
            CancellationToken.None);
        var checkpointAfterRetry = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);
        var outcomeAfterRetry = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);
        Assert.HasCount(1, incidentsAfterRetry.Value);
        Assert.AreEqual(incident.Id, incidentsAfterRetry.Value[0].Id);
        Assert.HasCount(1, checkpointAfterRetry.Value);
        Assert.AreEqual(next, checkpointAfterRetry.Value[0]);
        Assert.AreSame(OperationOutcome.Committed, outcomeAfterRetry.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-ANN-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task AnnotationAndExplicitReviewStatusRoundTripAndMissingIdsDoNotCommit()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "review-session");
        var baseline = CreateCheckpoint(session, counter: 0, time: 1_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, baseline).Value,
            CancellationToken.None)).IsSuccess);
        var incident = CreateIncident(session, total: 2, delta: 2, time: 2_000);
        var next = CreateCheckpoint(session, counter: 2, time: 2_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(incident, baseline, next).Value,
            CancellationToken.None)).IsSuccess);

        var annotation = IncidentAnnotation.TryCreate(
            "Unsafe rejoin",
            IncidentClassification.TryCreate(2).Value).Value;
        var annotate = AnnotateIncident.Create(
            incident.Id,
            annotation,
            UtcInstant.TryCreateUnixMilliseconds(3_000).Value);
        var reviewed = MarkIncidentReviewed.Create(
            incident.Id,
            UtcInstant.TryCreateUnixMilliseconds(4_000).Value);
        Assert.IsTrue((await context.Store.ExecuteAsync(annotate, CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(reviewed, CancellationToken.None)).IsSuccess);

        var actual = await context.Store.QueryAsync(new GetIncident(incident.Id), CancellationToken.None);
        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual(annotation, actual.Value.Value!.Annotation);
        Assert.AreEqual(IncidentReviewStatus.Reviewed, actual.Value.Value.ReviewStatus);

        var missing = IncidentId.Generate();
        var missingAnnotation = AnnotateIncident.Create(
            missing,
            annotation,
            UtcInstant.TryCreateUnixMilliseconds(5_000).Value);
        var missingReview = MarkIncidentReviewed.Create(
            missing,
            UtcInstant.TryCreateUnixMilliseconds(5_000).Value);
        AssertFailure(
            await context.Store.ExecuteAsync(missingAnnotation, CancellationToken.None),
            StoreErrorCodes.EntityNotFound,
            ErrorKind.NotFound);
        AssertFailure(
            await context.Store.ExecuteAsync(missingReview, CancellationToken.None),
            StoreErrorCodes.EntityNotFound,
            ErrorKind.NotFound);
        Assert.AreSame(
            OperationOutcome.NotCommitted,
            (await context.Store.QueryAsync(
                new GetOperationOutcome(missingReview.OperationId),
                CancellationToken.None)).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-ANN-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task AnnotationAndReviewTimestampsRemainValidWhenWallClockMovesBackward()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "rollback-clock-session");
        var baseline = CreateCheckpoint(session, counter: 0, time: 1_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, baseline).Value,
            CancellationToken.None)).IsSuccess);
        var incident = CreateIncident(session, total: 2, delta: 2, time: 2_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(
                incident,
                baseline,
                CreateCheckpoint(session, counter: 2, time: 2_000)).Value,
            CancellationToken.None)).IsSuccess);
        var annotation = IncidentAnnotation.TryCreate("Clock rollback", null).Value;

        Assert.IsTrue((await context.Store.ExecuteAsync(
            AnnotateIncident.Create(
                incident.Id,
                annotation,
                UtcInstant.TryCreateUnixMilliseconds(500).Value),
            CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            MarkIncidentReviewed.Create(
                incident.Id,
                UtcInstant.TryCreateUnixMilliseconds(600).Value),
            CancellationToken.None)).IsSuccess);

        var actual = await context.Store.QueryAsync(new GetIncident(incident.Id), CancellationToken.None);
        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual(annotation, actual.Value.Value!.Annotation);
        Assert.AreEqual(IncidentReviewStatus.Reviewed, actual.Value.Value.ReviewStatus);
        Assert.AreEqual(2_000, actual.Value.Value.UpdatedAt.UnixMilliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task SessionDetailsReturnsOneSnapshotInChronologicalIncidentOrder()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "ordered-session");
        var baseline = CreateCheckpoint(session, counter: 0, time: 1_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, baseline).Value,
            CancellationToken.None)).IsSuccess);

        var earlier = CreateIncident(session, total: 1, delta: 1, time: 2_000);
        var afterEarlier = CreateCheckpoint(session, counter: 1, time: 2_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(earlier, baseline, afterEarlier).Value,
            CancellationToken.None)).IsSuccess);
        var later = CreateIncident(session, total: 2, delta: 1, time: 3_000);
        var afterLater = CreateCheckpoint(session, counter: 2, time: 3_000);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(later, afterEarlier, afterLater).Value,
            CancellationToken.None)).IsSuccess);

        var details = await context.Store.QueryAsync(
            new GetSessionDetails(session),
            CancellationToken.None);

        Assert.IsTrue(details.IsSuccess);
        Assert.IsTrue(details.Value.IsFound);
        Assert.HasCount(2, details.Value.Value!.Incidents);
        Assert.AreEqual(earlier.Id, details.Value.Value.Incidents[0].Id);
        Assert.AreEqual(later.Id, details.Value.Value.Incidents[1].Id);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-STR-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task OneEventRoundTripsIncidentsAndCheckpointAcrossHeatNumbers()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "league-night-event");
        var firstBaseline = CreateCheckpoint(session, counter: 0, time: 100);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, firstBaseline).Value,
            CancellationToken.None)).IsSuccess);

        var firstCheckpoint = CreateCheckpoint(session, counter: 4, time: 500);
        var firstIncident = CreateIncident(session, total: 4, delta: 4, time: 500);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(
                firstIncident,
                firstBaseline,
                firstCheckpoint).Value,
            CancellationToken.None)).IsSuccess);

        var secondBaseline = CreateCheckpoint(
            session,
            counter: 0,
            time: 600,
            epoch: 1,
            sessionNumber: 2);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(firstCheckpoint, secondBaseline).Value,
            CancellationToken.None)).IsSuccess);
        var secondCheckpoint = CreateCheckpoint(
            session,
            counter: 2,
            time: 800,
            epoch: 1,
            sessionNumber: 2);
        var secondIncident = CreateIncident(
            session,
            total: 2,
            delta: 2,
            time: 800,
            epoch: 1,
            sessionNumber: 2);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(
                secondIncident,
                secondBaseline,
                secondCheckpoint).Value,
            CancellationToken.None)).IsSuccess);

        var details = await context.Store.QueryAsync(
            new GetSessionDetails(session),
            CancellationToken.None);
        var checkpoints = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);

        Assert.IsTrue(details.IsSuccess);
        Assert.HasCount(2, details.Value.Value!.Incidents);
        Assert.AreEqual(1, details.Value.Value.Incidents[0].Position.SessionNumber.Value);
        Assert.AreEqual(2, details.Value.Value.Incidents[1].Position.SessionNumber.Value);
        Assert.HasCount(1, checkpoints.Value);
        Assert.AreEqual(secondCheckpoint, checkpoints.Value[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task EqualCounterTotalsRemainIndependentAcrossParticipants()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "participant-scoped-session");
        var firstBaseline = CreateCheckpoint(
            session,
            counter: 0,
            time: 1_000,
            participantIdentity: "car:4:team:10");
        var secondBaseline = CreateCheckpoint(
            session,
            counter: 0,
            time: 1_000,
            participantIdentity: "car:7:team:20");
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, firstBaseline).Value,
            CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            EstablishIncidentCheckpoint.TryCreate(null, secondBaseline).Value,
            CancellationToken.None)).IsSuccess);

        var firstNext = CreateCheckpoint(
            session,
            counter: 4,
            time: 2_000,
            participantIdentity: "car:4:team:10");
        var secondNext = CreateCheckpoint(
            session,
            counter: 4,
            time: 2_000,
            participantIdentity: "car:7:team:20");
        var firstIncident = CreateIncident(
            session,
            total: 4,
            delta: 4,
            time: 2_000,
            participantIdentity: "car:4:team:10");
        var secondIncident = CreateIncident(
            session,
            total: 4,
            delta: 4,
            time: 2_000,
            participantIdentity: "car:7:team:20");

        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(firstIncident, firstBaseline, firstNext).Value,
            CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await context.Store.ExecuteAsync(
            RecordDetectedIncident.TryCreate(secondIncident, secondBaseline, secondNext).Value,
            CancellationToken.None)).IsSuccess);

        var incidents = await context.Store.QueryAsync(
            new GetIncidents(session),
            CancellationToken.None);
        var checkpoints = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);

        Assert.IsTrue(incidents.IsSuccess);
        Assert.HasCount(2, incidents.Value);
        CollectionAssert.AreEquivalent(
            new[] { firstIncident.Participant.Identity, secondIncident.Participant.Identity },
            incidents.Value.Select(static item => item.Participant.Identity).ToArray());
        Assert.AreEqual(
            firstIncident.Participant,
            incidents.Value.Single(item =>
                item.Participant.Identity == firstIncident.Participant.Identity).Participant);
        Assert.AreEqual(
            secondIncident.Participant,
            incidents.Value.Single(item =>
                item.Participant.Identity == secondIncident.Participant.Identity).Participant);
        Assert.IsTrue(checkpoints.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { firstNext, secondNext },
            checkpoints.Value.ToArray());

        using var connection = database.OpenConnection();
        Assert.AreEqual(2L, ExecuteScalarInt64(
            connection,
            """
            SELECT COUNT(*)
            FROM StoreOperation
            WHERE command_kind = 'incident-checkpoint.establish'
              AND command_version = 2;
            """));
        Assert.AreEqual(2L, ExecuteScalarInt64(
            connection,
            """
            SELECT COUNT(DISTINCT payload_fingerprint_sha256)
            FROM StoreOperation
            WHERE command_kind = 'incident-checkpoint.establish';
            """));
        Assert.AreEqual(2L, ExecuteScalarInt64(
            connection,
            """
            SELECT COUNT(*)
            FROM StoreOperation
            WHERE command_kind = 'incident.record-detected'
              AND command_version = 2;
            """));
    }

    private static async Task<SqliteTestStoreContext> CreateInitializedStore(
        TemporarySqliteDatabase database)
    {
        var context = SqliteTestingRegistration.CreateStore(database.Options);
        var result = await context.Initializer.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        return context;
    }

    private static async Task<SessionIdentity> EnsureSessionAsync(IStore store, string key)
    {
        var identity = SessionIdentity.Generate();
        var command = EnsureSession.Create(
            identity,
            CreateDescriptor(key),
            UtcInstant.TryCreateUnixMilliseconds(1_000).Value);
        Assert.IsTrue((await store.ExecuteAsync(command, CancellationToken.None)).IsSuccess);
        return identity;
    }

    private static SimulatorSessionDescriptor CreateDescriptor(string key) =>
        SimulatorSessionDescriptor.TryCreate(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate(key).Value,
            SessionNumber.TryCreate(1).Value,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;

    private static IncidentCheckpoint CreateCheckpoint(
        SessionIdentity session,
        int counter,
        long time,
        string participantIdentity = "participant-1",
        int epoch = 0,
        int sessionNumber = 1) => IncidentCheckpoint.TryCreate(
            session,
            ParticipantIdentity.TryCreate(participantIdentity).Value,
            CounterEpoch.TryCreate(epoch).Value,
            IncidentCounter.TryCreate(counter).Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(sessionNumber).Value,
                SessionTime.TryCreateMilliseconds(time).Value).Value,
            UtcInstant.TryCreateUnixMilliseconds(time).Value).Value;

    private static StoredIncident CreateIncident(
        SessionIdentity session,
        int total,
        int delta,
        long time,
        string participantIdentity = "participant-1",
        int epoch = 0,
        int sessionNumber = 1)
    {
        var instant = UtcInstant.TryCreateUnixMilliseconds(time).Value;
        return StoredIncident.Create(
            IncidentId.Generate(),
            session,
            IncidentParticipant.TryCreate(
                ParticipantIdentity.TryCreate(participantIdentity).Value,
                driverName: "Driver One",
                teamName: "Team One",
                carNumber: "01").Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(sessionNumber).Value,
                SessionTime.TryCreateMilliseconds(time).Value).Value,
            instant,
            IncidentPoints.TryCreate(total, delta).Value,
            CounterEpoch.TryCreate(epoch).Value,
            lap: null,
            lapDistance: null,
            IncidentReviewStatus.Pending,
            IncidentAnnotation.TryCreate(null, null).Value,
            instant,
            instant);
    }

    private static void AssertFailure(Result result, ErrorCode code, ErrorKind kind)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(kind, result.Error.Kind);
    }

    private static long ExecuteScalarInt64(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
