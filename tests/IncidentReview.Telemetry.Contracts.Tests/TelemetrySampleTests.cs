namespace IncidentReview.Telemetry.Contracts.Tests;

[TestClass]
public sealed class TelemetrySampleTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void TryCreatePreservesValidatedRequiredValues(int onTrackStateValue)
    {
        var session = TelemetryContractFixtures.CreateSession();
        var position = TelemetryContractFixtures.CreatePosition();
        var counter = TelemetryContractFixtures.CreateParticipantCounter();
        var observedAt = TelemetryContractFixtures.CreateObservedAt();
        var onTrackState = (OnTrackState)onTrackStateValue;

        var result = TelemetrySample.TryCreate(
            session,
            position,
            [counter],
            onTrackState,
            observedAt);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(session, result.Value.Session);
        Assert.AreSame(position, result.Value.Position);
        Assert.HasCount(1, result.Value.IncidentCounters);
        Assert.AreSame(counter, result.Value.IncidentCounters[0]);
        Assert.AreEqual(onTrackState, result.Value.OnTrackState);
        Assert.AreSame(observedAt, result.Value.ObservedAt);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void TryCreateRepresentsOptionalLapValuesIndependently(
        bool hasLap,
        bool hasLapDistance)
    {
        var counter = TelemetryContractFixtures.CreateParticipantCounter(
            lap: hasLap ? 7 : null,
            lapDistance: hasLapDistance ? 0.625 : null);

        var result = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            [counter],
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(counter.Lap, result.Value.IncidentCounters[0].Lap);
        Assert.AreSame(counter.LapDistance, result.Value.IncidentCounters[0].LapDistance);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow("session")]
    [DataRow("position")]
    [DataRow("counters")]
    [DataRow("observed-at")]
    public void TryCreateRejectsEachMissingRequiredValueIndependently(string missingValue)
    {
        SimulatorSessionDescriptor? session = TelemetryContractFixtures.CreateSession();
        ReplayPosition? position = TelemetryContractFixtures.CreatePosition();
        IEnumerable<ParticipantIncidentCounter>? counters =
            [TelemetryContractFixtures.CreateParticipantCounter()];
        UtcInstant? observedAt = TelemetryContractFixtures.CreateObservedAt();

        switch (missingValue)
        {
            case "session":
                session = null;
                break;
            case "position":
                position = null;
                break;
            case "counters":
                counters = null;
                break;
            case "observed-at":
                observedAt = null;
                break;
            default:
                Assert.Fail($"Unknown test dimension: {missingValue}");
                break;
        }

        var result = TelemetrySample.TryCreate(
            session,
            position,
            counters,
            OnTrackState.Unknown,
            observedAt);

        AssertInvalidSample(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void TryCreateRejectsMismatchedDescriptorAndPositionSessions()
    {
        var result = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(sessionNumber: 1),
            TelemetryContractFixtures.CreatePosition(sessionNumber: 2),
            [TelemetryContractFixtures.CreateParticipantCounter()],
            OnTrackState.Unknown,
            TelemetryContractFixtures.CreateObservedAt());

        AssertInvalidSample(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void TryCreateRejectsAnUndefinedOnTrackState()
    {
        var result = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            [TelemetryContractFixtures.CreateParticipantCounter()],
            (OnTrackState)int.MaxValue,
            TelemetryContractFixtures.CreateObservedAt());

        AssertInvalidSample(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "QR-ARC-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreateCopiesCountersAndPreservesTheirSourceOrder()
    {
        var first = TelemetryContractFixtures.CreateParticipantCounter("participant:2");
        var second = TelemetryContractFixtures.CreateParticipantCounter("participant:1");
        var source = new List<ParticipantIncidentCounter> { first, second };

        var sample = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            source,
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt()).Value;
        source.Clear();

        CollectionAssert.AreEqual(
            new[] { first, second },
            sample.IncidentCounters.ToArray());
        var mutableView = (IList<ParticipantIncidentCounter>)sample.IncidentCounters;
        Assert.ThrowsExactly<NotSupportedException>(() => mutableView[0] = second);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreateAllowsNoParticipantObservations()
    {
        var result = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            [],
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(result.Value.IncidentCounters);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreateRejectsNullDuplicateOrExcessiveCounterCollections()
    {
        var duplicateIdentity = ParticipantIdentity.TryCreate("participant:duplicate").Value;
        var firstParticipant = IncidentParticipant.TryCreate(
            duplicateIdentity,
            "First Driver",
            null,
            null).Value;
        var secondParticipant = IncidentParticipant.TryCreate(
            duplicateIdentity,
            "Second Driver",
            null,
            null).Value;
        var duplicateCounters = new[]
        {
            ParticipantIncidentCounter.TryCreate(
                firstParticipant,
                IncidentCounter.TryCreate(0).Value,
                null,
                null).Value,
            ParticipantIncidentCounter.TryCreate(
                secondParticipant,
                IncidentCounter.TryCreate(1).Value,
                null,
                null).Value,
        };

        AssertInvalidCounters([null!]);
        AssertInvalidCounters(duplicateCounters);

        var maximum = Enumerable.Range(0, TelemetrySample.MaximumIncidentCounterCount)
            .Select(index => TelemetryContractFixtures.CreateParticipantCounter(
                $"participant:{index}"))
            .ToList();
        var accepted = CreateSample(maximum);
        Assert.IsTrue(accepted.IsSuccess);

        maximum.Add(TelemetryContractFixtures.CreateParticipantCounter("participant:excess"));
        AssertInvalidSample(CreateSample(maximum));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void SampleCannotBeConstructedOrMutatedIntoAnInvalidState()
    {
        Assert.IsTrue(typeof(TelemetrySample).IsSealed);
        Assert.IsEmpty(typeof(TelemetrySample).GetConstructors());
        Assert.IsTrue(typeof(TelemetrySample).GetProperties().All(property =>
            property.SetMethod is null));
    }

    private static void AssertInvalidSample(Result<TelemetrySample> result)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(TelemetryErrorCodes.InvalidSample, result.Error.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
    }

    private static Result<TelemetrySample> CreateSample(
        IEnumerable<ParticipantIncidentCounter> counters) => TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            counters,
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt());

    private static void AssertInvalidCounters(
        IEnumerable<ParticipantIncidentCounter> counters) =>
        AssertInvalidSample(CreateSample(counters));
}
