using IncidentReview.Domain;
using IncidentReview.Store.Contracts;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class ApplicationStoreContractTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ApplicationQueriesAndCommandsAreClosedImmutableContractValues()
    {
        Type[] contractTypes =
        [
            typeof(GetSessionBySimulatorKey),
            typeof(GetSession),
            typeof(ListSessions),
            typeof(GetSessionDetails),
            typeof(GetIncident),
            typeof(GetIncidents),
            typeof(GetIncidentCheckpoints),
            typeof(EnsureSession),
            typeof(PromoteSessionIdentity),
            typeof(EstablishIncidentCheckpoint),
            typeof(RecordDetectedIncident),
            typeof(AnnotateIncident),
            typeof(MarkIncidentReviewed),
        ];

        foreach (var type in contractTypes)
        {
            Assert.IsTrue(type.IsSealed, type.FullName);
            Assert.IsTrue(type.GetProperties().All(static property => property.SetMethod is null), type.FullName);
        }

        Assert.IsTrue(contractTypes.Take(7).All(type =>
            type.GetInterfaces().Any(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IStoreQuery<>))));
        Assert.IsTrue(contractTypes.Skip(7).All(type => typeof(IStoreCommand).IsAssignableFrom(type)));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    public void PromotionRequiresALegacyIdentityAndDurableEvidence()
    {
        var durable = CreateDescriptor("promote-durable");
        var connectionScoped = SimulatorSessionDescriptor.TryCreate(
            durable.Simulator,
            durable.SessionKey,
            durable.SessionNumber,
            durable.Mode,
            SimulatorIdentityScope.ConnectionScoped).Value;
        var promotedAt = UtcInstant.TryCreateUnixMilliseconds(2_000).Value;

        var valid = PromoteSessionIdentity.TryCreate(
            SessionIdentity.Generate(),
            durable,
            promotedAt);
        var alreadyDeterministic = PromoteSessionIdentity.TryCreate(
            SessionIdentity.CreateDurable(durable.Simulator, durable.SessionKey),
            durable,
            promotedAt);
        var transient = PromoteSessionIdentity.TryCreate(
            SessionIdentity.Generate(),
            connectionScoped,
            promotedAt);

        Assert.IsTrue(valid.IsSuccess);
        Assert.AreEqual(
            SessionIdentity.CreateDurable(durable.Simulator, durable.SessionKey),
            valid.Value.DeterministicIdentity);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, alreadyDeterministic.Error!.Code);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, transient.Error!.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void StoreLookupRequiresExplicitFoundOrMissingConstruction()
    {
        var value = new object();
        var found = StoreLookup.Found(value);
        var missing = StoreLookup.Missing<object>();

        Assert.IsTrue(found.IsFound);
        Assert.AreSame(value, found.Value);
        Assert.IsFalse(missing.IsFound);
        Assert.IsNull(missing.Value);
        Assert.IsEmpty(typeof(StoreLookup<object>).GetConstructors());
        Assert.ThrowsExactly<ArgumentNullException>(() => StoreLookup.Found<object>(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void RecordCommandRejectsCrossSessionAndCounterInconsistency()
    {
        var firstSession = SessionIdentity.Generate();
        var secondSession = SessionIdentity.Generate();
        var expected = CreateCheckpoint(firstSession, counter: 1);
        var next = CreateCheckpoint(firstSession, counter: 3);
        var wrongSession = CreateIncident(secondSession, total: 3, delta: 2);
        var wrongTotal = CreateIncident(firstSession, total: 2, delta: 1);
        var wrongParticipant = CreateIncident(
            firstSession,
            total: 3,
            delta: 2,
            participantIdentity: "participant-2");

        var crossSession = RecordDetectedIncident.TryCreate(wrongSession, expected, next);
        var inconsistentTotal = RecordDetectedIncident.TryCreate(wrongTotal, expected, next);
        var crossParticipant = RecordDetectedIncident.TryCreate(wrongParticipant, expected, next);

        Assert.IsFalse(crossSession.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, crossSession.Error!.Code);
        Assert.IsFalse(inconsistentTotal.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, inconsistentTotal.Error!.Code);
        Assert.IsFalse(crossParticipant.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, crossParticipant.Error!.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    public void RecordCommandRequiresOneExactPositiveCheckpointTransition()
    {
        var session = SessionIdentity.Generate();
        var expected = CreateCheckpoint(session, counter: 1, epoch: 0, time: 1_000);
        var next = CreateCheckpoint(session, counter: 3, epoch: 0, time: 2_000);
        var valid = CreateIncident(session, total: 3, delta: 2, epoch: 0, positionTime: 2_000, observedAt: 2_000);

        Assert.IsTrue(RecordDetectedIncident.TryCreate(valid, expected, next).IsSuccess);
        AssertInvalidRecord(CreateIncident(
            session,
            total: 3,
            delta: 2,
            epoch: 1,
            positionTime: 2_000,
            observedAt: 2_000),
            expected,
            CreateCheckpoint(session, counter: 3, epoch: 1, time: 2_000));
        AssertInvalidRecord(
            CreateIncident(session, total: 1, delta: 1, epoch: 0, positionTime: 2_000, observedAt: 2_000),
            expected,
            CreateCheckpoint(session, counter: 1, epoch: 0, time: 2_000));
        AssertInvalidRecord(
            CreateIncident(session, total: 3, delta: 1, epoch: 0, positionTime: 2_000, observedAt: 2_000),
            expected,
            next);
        AssertInvalidRecord(
            CreateIncident(session, total: 3, delta: 2, epoch: 0, positionTime: 2_500, observedAt: 2_000),
            expected,
            next);
        AssertInvalidRecord(
            CreateIncident(session, total: 3, delta: 2, epoch: 0, positionTime: 2_000, observedAt: 2_500),
            expected,
            next);
        AssertInvalidRecord(
            CreateIncident(
                session,
                total: 3,
                delta: 2,
                epoch: 0,
                positionTime: 2_000,
                observedAt: 2_000,
                positionSessionNumber: 2),
            expected,
            CreateCheckpoint(session, counter: 3, epoch: 0, time: 2_000, positionSessionNumber: 2));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void SessionDetailsRejectsIncidentsFromAnotherSession()
    {
        var session = SessionIdentity.Generate();
        var storedSession = StoredSession.Create(
            session,
            SimulatorSessionDescriptor.TryCreate(
                SimulatorCode.TryCreate("iracing").Value,
                SimulatorSessionKey.TryCreate("session-key").Value,
                SessionNumber.TryCreate(1).Value,
                SessionMode.Live,
                SimulatorIdentityScope.Durable).Value,
            UtcInstant.TryCreateUnixMilliseconds(0).Value);
        var wrongIncident = CreateIncident(SessionIdentity.Generate(), total: 1, delta: 1);

        Assert.ThrowsExactly<ArgumentException>(() =>
            StoredSessionDetails.Create(storedSession, [wrongIncident]));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-005")]
    public void BaselineCommandAllowsInitialCounterResetOrNewHeatTransitions()
    {
        var session = SessionIdentity.Generate();
        var initial = CreateCheckpoint(session, counter: 4, epoch: 0, time: 1_000);
        var reset = CreateCheckpoint(session, counter: 1, epoch: 1, time: 2_000);

        Assert.IsTrue(EstablishIncidentCheckpoint.TryCreate(null, initial).IsSuccess);
        Assert.IsTrue(EstablishIncidentCheckpoint.TryCreate(initial, reset).IsSuccess);
        AssertInvalidBaseline(
            initial,
            CreateCheckpoint(
                session,
                counter: 1,
                epoch: 1,
                time: 2_000,
                participantIdentity: "participant-2"));
        AssertInvalidBaseline(null, CreateCheckpoint(session, counter: 4, epoch: 1, time: 1_000));
        AssertInvalidBaseline(initial, CreateCheckpoint(session, counter: 5, epoch: 1, time: 2_000));
        AssertInvalidBaseline(initial, CreateCheckpoint(session, counter: 1, epoch: 2, time: 2_000));
        var otherSessionNumber = IncidentCheckpoint.TryCreate(
            session,
            ParticipantIdentity.TryCreate("participant-1").Value,
            CounterEpoch.TryCreate(1).Value,
            IncidentCounter.TryCreate(5).Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(2).Value,
                SessionTime.TryCreateMilliseconds(2_000).Value).Value,
            UtcInstant.TryCreateUnixMilliseconds(2_000).Value).Value;
        Assert.IsTrue(EstablishIncidentCheckpoint.TryCreate(initial, otherSessionNumber).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public void ExplicitReviewStatusCommandCarriesOnlyIntentAndStableOperationIdentity()
    {
        var incident = IncidentId.Generate();
        var reviewedAt = UtcInstant.TryCreateUnixMilliseconds(2_000).Value;

        var command = MarkIncidentReviewed.Create(incident, reviewedAt);

        Assert.AreSame(incident, command.Incident);
        Assert.AreSame(reviewedAt, command.ReviewedAt);
        Assert.IsTrue(OperationId.TryParse(command.OperationId.ToString()).IsSuccess);
    }

    private static IncidentCheckpoint CreateCheckpoint(
        SessionIdentity session,
        int counter,
        int epoch = 0,
        long? time = null,
        int positionSessionNumber = 1,
        string participantIdentity = "participant-1") =>
        IncidentCheckpoint.TryCreate(
            session,
            ParticipantIdentity.TryCreate(participantIdentity).Value,
            CounterEpoch.TryCreate(epoch).Value,
            IncidentCounter.TryCreate(counter).Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(positionSessionNumber).Value,
                SessionTime.TryCreateMilliseconds(time ?? counter * 1_000L).Value).Value,
            UtcInstant.TryCreateUnixMilliseconds(time ?? counter * 1_000L).Value).Value;

    private static StoredIncident CreateIncident(
        SessionIdentity session,
        int total,
        int delta,
        int epoch = 0,
        long? positionTime = null,
        long? observedAt = null,
        int positionSessionNumber = 1,
        string participantIdentity = "participant-1")
    {
        var positionMilliseconds = positionTime ?? total * 1_000L;
        var instant = UtcInstant.TryCreateUnixMilliseconds(observedAt ?? positionMilliseconds).Value;
        return StoredIncident.Create(
            IncidentId.Generate(),
            session,
            IncidentParticipant.TryCreate(
                ParticipantIdentity.TryCreate(participantIdentity).Value,
                driverName: "Driver",
                teamName: "Team",
                carNumber: "7").Value,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(positionSessionNumber).Value,
                SessionTime.TryCreateMilliseconds(positionMilliseconds).Value).Value,
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

    private static SimulatorSessionDescriptor CreateDescriptor(string sessionKey) =>
        SimulatorSessionDescriptor.TryCreate(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate(sessionKey).Value,
            SessionNumber.TryCreate(1).Value,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;

    private static void AssertInvalidRecord(
        StoredIncident incident,
        IncidentCheckpoint expected,
        IncidentCheckpoint next)
    {
        var result = RecordDetectedIncident.TryCreate(incident, expected, next);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, result.Error!.Code);
    }

    private static void AssertInvalidBaseline(
        IncidentCheckpoint? expected,
        IncidentCheckpoint next)
    {
        var result = EstablishIncidentCheckpoint.TryCreate(expected, next);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(StoreErrorCodes.InvalidCommand, result.Error!.Code);
    }
}
