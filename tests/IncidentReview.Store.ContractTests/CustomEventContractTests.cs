using IncidentReview.Domain;
using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class CustomEventContractTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public void PendingEventOwnsItsDeterministicIdentityAndOutboxState()
    {
        var session = CreateSession();
        var position = CreatePosition(milliseconds: 12_345);
        var submitter = SubmitterName.TryCreate("Casey Driver").Value;
        var occurredAt = CreateInstant(100_000);

        var customEvent = StoredCustomEvent.CreatePending(
            session,
            position,
            submitter,
            occurredAt);

        Assert.AreEqual(CustomEventId.CreateDeterministic(session, position), customEvent.Id);
        Assert.AreSame(session, customEvent.Session);
        Assert.AreSame(position, customEvent.Position);
        Assert.AreSame(submitter, customEvent.Submitter);
        Assert.AreSame(occurredAt, customEvent.OccurredAt);
        Assert.AreSame(CustomEventSynchronization.Pending.Instance, customEvent.Synchronization);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void StoredEventRejectsAnIdentityThatDoesNotMatchItsReplayPosition()
    {
        var session = CreateSession();
        var originalPosition = CreatePosition(milliseconds: 12_345);
        var changedPosition = CreatePosition(milliseconds: 12_346);
        var id = CustomEventId.CreateDeterministic(session, originalPosition);

        Assert.ThrowsExactly<ArgumentException>(() => StoredCustomEvent.Create(
            id,
            session,
            changedPosition,
            SubmitterName.TryCreate("Casey Driver").Value,
            CreateInstant(100_000),
            CustomEventSynchronization.Pending.Instance));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SynchronizedStateRequiresAConfirmationNoEarlierThanOccurrence()
    {
        var pending = CreatePendingEvent(occurredAt: 100_000);
        var synchronizedAt = CreateInstant(100_001);
        var synchronization = CustomEventSynchronization.Synchronized.Create(synchronizedAt);

        var synchronized = StoredCustomEvent.Create(
            pending.Id,
            pending.Session,
            pending.Position,
            pending.Submitter,
            pending.OccurredAt,
            synchronization);

        Assert.AreSame(synchronization, synchronized.Synchronization);
        Assert.AreSame(
            synchronizedAt,
            ((CustomEventSynchronization.Synchronized)synchronized.Synchronization)
                .SynchronizedAt);
        Assert.ThrowsExactly<ArgumentException>(() => StoredCustomEvent.Create(
            pending.Id,
            pending.Session,
            pending.Position,
            pending.Submitter,
            pending.OccurredAt,
            CustomEventSynchronization.Synchronized.Create(CreateInstant(99_999))));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            CustomEventSynchronization.Synchronized.Create(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-STR-005")]
    public void RecordCommandAcceptsOnlyPendingEventsAndRetainsOneRetryIdentity()
    {
        var pending = CreatePendingEvent();
        var first = RecordCustomEvent.TryCreate(pending).Value;
        var second = RecordCustomEvent.TryCreate(pending).Value;

        Assert.AreSame(pending, first.CustomEvent);
        Assert.AreNotEqual(first.OperationId, second.OperationId);
        Assert.IsInstanceOfType<IStoreCommand>(first);
        Assert.IsTrue(OperationId.TryParse(first.OperationId.ToString()).IsSuccess);

        var synchronized = StoredCustomEvent.Create(
            pending.Id,
            pending.Session,
            pending.Position,
            pending.Submitter,
            pending.OccurredAt,
            CustomEventSynchronization.Synchronized.Create(CreateInstant(100_001)));
        AssertInvalidRecord(RecordCustomEvent.TryCreate(synchronized));
        AssertInvalidRecord(RecordCustomEvent.TryCreate(null));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "IR-STR-005")]
    public void MarkSynchronizedCommandCarriesOnlyIdentityAndConfirmationTime()
    {
        var pending = CreatePendingEvent();
        var synchronizedAt = CreateInstant(200_000);

        var first = MarkCustomEventSynchronized.Create(pending.Id, synchronizedAt);
        var second = MarkCustomEventSynchronized.Create(pending.Id, synchronizedAt);

        Assert.AreSame(pending.Id, first.CustomEvent);
        Assert.AreSame(synchronizedAt, first.SynchronizedAt);
        Assert.AreNotEqual(first.OperationId, second.OperationId);
        Assert.IsInstanceOfType<IStoreCommand>(first);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            MarkCustomEventSynchronized.Create(null!, synchronizedAt));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            MarkCustomEventSynchronized.Create(pending.Id, null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    [TestProperty("Requirement", "IR-STR-005")]
    public void ReceivedEventCommandAcceptsOnlyAlreadySynchronizedEvents()
    {
        var pending = CreatePendingEvent();
        var synchronized = StoredCustomEvent.Create(
            pending.Id,
            pending.Session,
            pending.Position,
            pending.Submitter,
            pending.OccurredAt,
            CustomEventSynchronization.Synchronized.Create(CreateInstant(100_001)));

        var command = RecordReceivedCustomEvent.TryCreate(synchronized).Value;

        Assert.AreSame(synchronized, command.CustomEvent);
        Assert.IsInstanceOfType<IStoreCommand>(command);
        Assert.IsTrue(OperationId.TryParse(command.OperationId.ToString()).IsSuccess);
        AssertInvalidReceivedRecord(RecordReceivedCustomEvent.TryCreate(pending));
        AssertInvalidReceivedRecord(RecordReceivedCustomEvent.TryCreate(null));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void CustomEventQueriesAreSessionScopedImmutableValues()
    {
        var pending = CreatePendingEvent();
        var lookup = new GetCustomEvent(pending.Id);
        var all = new GetCustomEvents(pending.Session);
        var outbox = new GetPendingCustomEvents(pending.Session);

        Assert.AreSame(pending.Id, lookup.CustomEvent);
        Assert.AreSame(pending.Session, all.Session);
        Assert.AreSame(pending.Session, outbox.Session);
        Assert.IsInstanceOfType<IStoreQuery<StoreLookup<StoredCustomEvent>>>(lookup);
        Assert.IsInstanceOfType<IStoreQuery<IReadOnlyList<StoredCustomEvent>>>(all);
        Assert.IsInstanceOfType<IStoreQuery<IReadOnlyList<StoredCustomEvent>>>(outbox);

        Type[] types =
        [
            typeof(StoredCustomEvent),
            typeof(GetCustomEvent),
            typeof(GetCustomEvents),
            typeof(GetPendingCustomEvents),
            typeof(RecordCustomEvent),
            typeof(RecordReceivedCustomEvent),
            typeof(MarkCustomEventSynchronized),
        ];
        foreach (var type in types)
        {
            Assert.IsTrue(type.IsSealed, type.FullName);
            Assert.IsTrue(
                type.GetProperties().All(static property => property.SetMethod is null),
                type.FullName);
        }

        Assert.ThrowsExactly<ArgumentNullException>(() => new GetCustomEvent(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new GetCustomEvents(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new GetPendingCustomEvents(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void SynchronizationStateIsAClosedImmutableHierarchy()
    {
        Assert.IsTrue(typeof(CustomEventSynchronization).IsAbstract);
        Assert.IsTrue(typeof(CustomEventSynchronization.Pending).IsSealed);
        Assert.IsTrue(typeof(CustomEventSynchronization.Synchronized).IsSealed);
        Assert.IsEmpty(typeof(CustomEventSynchronization).GetConstructors());
        Assert.IsEmpty(typeof(CustomEventSynchronization.Pending).GetConstructors());
        Assert.IsEmpty(typeof(CustomEventSynchronization.Synchronized).GetConstructors());
        var instanceProperty = typeof(CustomEventSynchronization.Pending)
            .GetProperty(nameof(CustomEventSynchronization.Pending.Instance))!;
        Assert.AreSame(
            instanceProperty.GetValue(null),
            instanceProperty.GetValue(null));
    }

    private static StoredCustomEvent CreatePendingEvent(long occurredAt = 100_000) =>
        StoredCustomEvent.CreatePending(
            CreateSession(),
            CreatePosition(milliseconds: 12_345),
            SubmitterName.TryCreate("Casey Driver").Value,
            CreateInstant(occurredAt));

    private static SessionIdentity CreateSession() =>
        SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("v1:subsession:123456:session:0").Value);

    private static ReplayPosition CreatePosition(long milliseconds) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(0).Value,
            SessionTime.TryCreateMilliseconds(milliseconds).Value).Value;

    private static UtcInstant CreateInstant(long milliseconds) =>
        UtcInstant.TryCreateUnixMilliseconds(milliseconds).Value;

    private static void AssertInvalidRecord(Result<RecordCustomEvent> result)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, result.Error!.Code);
    }

    private static void AssertInvalidReceivedRecord(Result<RecordReceivedCustomEvent> result)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, result.Error!.Code);
    }
}
