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
            new GetIncidentCheckpoint(session),
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
        Assert.AreEqual(next, checkpoint.Value.Value);
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
            new GetIncidentCheckpoint(session),
            CancellationToken.None);
        var outcomeAfterCancellation = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);
        Assert.IsTrue(incidentsAfterCancellation.IsSuccess);
        Assert.IsEmpty(incidentsAfterCancellation.Value);
        Assert.AreEqual(baseline, checkpointAfterCancellation.Value.Value);
        Assert.AreSame(OperationOutcome.NotCommitted, outcomeAfterCancellation.Value);

        Assert.IsTrue((await context.Store.ExecuteAsync(command, CancellationToken.None)).IsSuccess);
        var incidentsAfterRetry = await context.Store.QueryAsync(
            new GetIncidents(session),
            CancellationToken.None);
        var checkpointAfterRetry = await context.Store.QueryAsync(
            new GetIncidentCheckpoint(session),
            CancellationToken.None);
        var outcomeAfterRetry = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);
        Assert.HasCount(1, incidentsAfterRetry.Value);
        Assert.AreEqual(incident.Id, incidentsAfterRetry.Value[0].Id);
        Assert.AreEqual(next, checkpointAfterRetry.Value.Value);
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
        long time) => IncidentCheckpoint.TryCreate(
            session,
            CounterEpoch.TryCreate(0).Value,
            IncidentCounter.TryCreate(counter).Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(1).Value,
                SessionTime.TryCreateMilliseconds(time).Value).Value,
            UtcInstant.TryCreateUnixMilliseconds(time).Value).Value;

    private static StoredIncident CreateIncident(
        SessionIdentity session,
        int total,
        int delta,
        long time)
    {
        var instant = UtcInstant.TryCreateUnixMilliseconds(time).Value;
        return StoredIncident.Create(
            IncidentId.Generate(),
            session,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(1).Value,
                SessionTime.TryCreateMilliseconds(time).Value).Value,
            instant,
            IncidentPoints.TryCreate(total, delta).Value,
            CounterEpoch.TryCreate(0).Value,
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
}
