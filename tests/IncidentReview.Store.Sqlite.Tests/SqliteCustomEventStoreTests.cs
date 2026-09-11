using IncidentReview.Domain;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Testing;

namespace IncidentReview.Store.Sqlite.Tests;

[TestClass]
public sealed class SqliteCustomEventStoreTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public async Task PendingEventRoundTripsThroughEventOutboxAndSessionSnapshot()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "pending-round-trip");
        var customEvent = CreatePendingEvent(
            session,
            replaySessionNumber: 2,
            replaySessionTimeMilliseconds: 12_345,
            submitter: "Driver One",
            occurredAtMilliseconds: 50_000);
        var record = RecordCustomEvent.TryCreate(customEvent);

        Assert.IsTrue(record.IsSuccess, record.Error?.ToString());
        Assert.IsTrue((await context.Store.ExecuteAsync(
            record.Value,
            CancellationToken.None)).IsSuccess);

        var byIdentity = await context.Store.QueryAsync(
            new GetCustomEvent(customEvent.Id),
            CancellationToken.None);
        var allForSession = await context.Store.QueryAsync(
            new GetCustomEvents(session),
            CancellationToken.None);
        var pendingForSession = await context.Store.QueryAsync(
            new GetPendingCustomEvents(session),
            CancellationToken.None);
        var sessionDetails = await context.Store.QueryAsync(
            new GetSessionDetails(session),
            CancellationToken.None);

        Assert.IsTrue(byIdentity.IsSuccess, byIdentity.Error?.ToString());
        Assert.IsTrue(byIdentity.Value.IsFound);
        Assert.AreEqual(customEvent, byIdentity.Value.Value);
        Assert.AreSame(
            CustomEventSynchronization.Pending.Instance,
            byIdentity.Value.Value!.Synchronization);
        Assert.IsTrue(allForSession.IsSuccess, allForSession.Error?.ToString());
        Assert.HasCount(1, allForSession.Value);
        Assert.AreEqual(customEvent, allForSession.Value[0]);
        Assert.IsTrue(pendingForSession.IsSuccess, pendingForSession.Error?.ToString());
        Assert.HasCount(1, pendingForSession.Value);
        Assert.AreEqual(customEvent, pendingForSession.Value[0]);
        Assert.IsTrue(sessionDetails.IsSuccess, sessionDetails.Error?.ToString());
        Assert.IsTrue(sessionDetails.Value.IsFound);
        Assert.HasCount(1, sessionDetails.Value.Value!.CustomEvents);
        Assert.AreEqual(customEvent, sessionDetails.Value.Value.CustomEvents[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public async Task MarkSynchronizedRemovesEventFromOutboxButPreservesItInHistory()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "synchronized-round-trip");
        var customEvent = CreatePendingEvent(
            session,
            replaySessionNumber: 3,
            replaySessionTimeMilliseconds: 23_456,
            submitter: "Driver Two",
            occurredAtMilliseconds: 60_000);
        var record = RecordCustomEvent.TryCreate(customEvent);
        Assert.IsTrue(record.IsSuccess, record.Error?.ToString());
        Assert.IsTrue((await context.Store.ExecuteAsync(
            record.Value,
            CancellationToken.None)).IsSuccess);
        var synchronizedAt = UtcInstant.TryCreateUnixMilliseconds(61_000).Value;
        var synchronize = MarkCustomEventSynchronized.Create(customEvent.Id, synchronizedAt);

        var result = await context.Store.ExecuteAsync(synchronize, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var byIdentity = await context.Store.QueryAsync(
            new GetCustomEvent(customEvent.Id),
            CancellationToken.None);
        var allForSession = await context.Store.QueryAsync(
            new GetCustomEvents(session),
            CancellationToken.None);
        var pendingForSession = await context.Store.QueryAsync(
            new GetPendingCustomEvents(session),
            CancellationToken.None);

        Assert.IsTrue(byIdentity.IsSuccess, byIdentity.Error?.ToString());
        Assert.IsTrue(byIdentity.Value.IsFound);
        var synchronized = Assert.IsInstanceOfType<CustomEventSynchronization.Synchronized>(
            byIdentity.Value.Value!.Synchronization);
        Assert.AreEqual(synchronizedAt, synchronized.SynchronizedAt);
        Assert.IsTrue(allForSession.IsSuccess, allForSession.Error?.ToString());
        Assert.HasCount(1, allForSession.Value);
        Assert.IsInstanceOfType<CustomEventSynchronization.Synchronized>(
            allForSession.Value[0].Synchronization);
        Assert.IsTrue(pendingForSession.IsSuccess, pendingForSession.Error?.ToString());
        Assert.IsEmpty(pendingForSession.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task ReceivedEventIsRecordedAtomicallyAsSynchronized()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "received-round-trip");
        var pending = CreatePendingEvent(
            session,
            replaySessionNumber: 5,
            replaySessionTimeMilliseconds: 45_678,
            submitter: "Remote Driver",
            occurredAtMilliseconds: 80_000);
        var synchronizedAt = UtcInstant.TryCreateUnixMilliseconds(81_000).Value;
        var received = StoredCustomEvent.Create(
            pending.Id,
            pending.Session,
            pending.Position,
            pending.Submitter,
            pending.OccurredAt,
            CustomEventSynchronization.Synchronized.Create(synchronizedAt));
        var command = RecordReceivedCustomEvent.TryCreate(received);

        Assert.IsTrue(command.IsSuccess, command.Error?.ToString());
        var result = await context.Store.ExecuteAsync(command.Value, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var byIdentity = await context.Store.QueryAsync(
            new GetCustomEvent(received.Id),
            CancellationToken.None);
        var pendingForSession = await context.Store.QueryAsync(
            new GetPendingCustomEvents(session),
            CancellationToken.None);
        Assert.IsTrue(byIdentity.IsSuccess, byIdentity.Error?.ToString());
        Assert.IsTrue(byIdentity.Value.IsFound);
        Assert.AreEqual(received, byIdentity.Value.Value);
        var synchronization = Assert.IsInstanceOfType<CustomEventSynchronization.Synchronized>(
            byIdentity.Value.Value!.Synchronization);
        Assert.AreEqual(synchronizedAt, synchronization.SynchronizedAt);
        Assert.IsTrue(pendingForSession.IsSuccess, pendingForSession.Error?.ToString());
        Assert.IsEmpty(pendingForSession.Value);

        var duplicate = StoredCustomEvent.Create(
            received.Id,
            received.Session,
            received.Position,
            SubmitterName.TryCreate("Later Driver").Value,
            UtcInstant.TryCreateUnixMilliseconds(82_000).Value,
            CustomEventSynchronization.Synchronized.Create(
                UtcInstant.TryCreateUnixMilliseconds(82_001).Value));
        var duplicateCommand = RecordReceivedCustomEvent.TryCreate(duplicate).Value;
        var duplicateResult = await context.Store.ExecuteAsync(
            duplicateCommand,
            CancellationToken.None);

        Assert.IsFalse(duplicateResult.IsSuccess);
        Assert.AreEqual(
            StoreErrorCodes.CustomEventIdentityConflict,
            duplicateResult.Error!.Code);
        var retained = await context.Store.QueryAsync(
            new GetCustomEvent(received.Id),
            CancellationToken.None);
        Assert.AreEqual(received, retained.Value.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-001")]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task SameDeterministicIdentityKeepsFirstCommittedPayload()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var session = await EnsureSessionAsync(context.Store, "deterministic-duplicate");
        var first = CreatePendingEvent(
            session,
            replaySessionNumber: 4,
            replaySessionTimeMilliseconds: 34_567,
            submitter: "First Driver",
            occurredAtMilliseconds: 70_000);
        var duplicate = CreatePendingEvent(
            session,
            replaySessionNumber: 4,
            replaySessionTimeMilliseconds: 34_567,
            submitter: "Later Driver",
            occurredAtMilliseconds: 71_000);
        Assert.AreEqual(first.Id, duplicate.Id);
        Assert.AreNotEqual(first.Submitter, duplicate.Submitter);
        Assert.AreNotEqual(first.OccurredAt, duplicate.OccurredAt);
        var firstCommand = RecordCustomEvent.TryCreate(first);
        var duplicateCommand = RecordCustomEvent.TryCreate(duplicate);
        Assert.IsTrue(firstCommand.IsSuccess, firstCommand.Error?.ToString());
        Assert.IsTrue(duplicateCommand.IsSuccess, duplicateCommand.Error?.ToString());

        var firstResult = await context.Store.ExecuteAsync(
            firstCommand.Value,
            CancellationToken.None);
        var duplicateResult = await context.Store.ExecuteAsync(
            duplicateCommand.Value,
            CancellationToken.None);

        Assert.IsTrue(firstResult.IsSuccess, firstResult.Error?.ToString());
        Assert.IsTrue(duplicateResult.IsSuccess, duplicateResult.Error?.ToString());
        var actual = await context.Store.QueryAsync(
            new GetCustomEvent(first.Id),
            CancellationToken.None);
        var allForSession = await context.Store.QueryAsync(
            new GetCustomEvents(session),
            CancellationToken.None);

        Assert.IsTrue(actual.IsSuccess, actual.Error?.ToString());
        Assert.IsTrue(actual.Value.IsFound);
        Assert.AreEqual(first, actual.Value.Value);
        Assert.AreEqual("First Driver", actual.Value.Value!.Submitter.Value);
        Assert.AreEqual(70_000L, actual.Value.Value.OccurredAt.UnixMilliseconds);
        Assert.IsTrue(allForSession.IsSuccess, allForSession.Error?.ToString());
        Assert.HasCount(1, allForSession.Value);
        Assert.AreEqual(first, allForSession.Value[0]);
    }

    private static async Task<SqliteTestStoreContext> CreateInitializedStore(
        TemporarySqliteDatabase database)
    {
        var context = SqliteTestingRegistration.CreateStore(database.Options);
        var result = await context.Initializer.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        return context;
    }

    private static async Task<SessionIdentity> EnsureSessionAsync(IStore store, string key)
    {
        var simulator = SimulatorCode.TryCreate("iracing").Value;
        var sessionKey = SimulatorSessionKey.TryCreate(key).Value;
        var identity = SessionIdentity.CreateDurable(simulator, sessionKey);
        var descriptor = SimulatorSessionDescriptor.TryCreate(
            simulator,
            sessionKey,
            SessionNumber.TryCreate(1).Value,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;
        var command = EnsureSession.Create(
            identity,
            descriptor,
            UtcInstant.TryCreateUnixMilliseconds(1_000).Value);
        var result = await store.ExecuteAsync(command, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        return identity;
    }

    private static StoredCustomEvent CreatePendingEvent(
        SessionIdentity session,
        int replaySessionNumber,
        long replaySessionTimeMilliseconds,
        string submitter,
        long occurredAtMilliseconds) => StoredCustomEvent.CreatePending(
        session,
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(replaySessionNumber).Value,
            SessionTime.TryCreateMilliseconds(replaySessionTimeMilliseconds).Value).Value,
        SubmitterName.TryCreate(submitter).Value,
        UtcInstant.TryCreateUnixMilliseconds(occurredAtMilliseconds).Value);
}
