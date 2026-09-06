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
        var counter = TelemetryContractFixtures.CreateCounter();
        var observedAt = TelemetryContractFixtures.CreateObservedAt();
        var onTrackState = (OnTrackState)onTrackStateValue;

        var result = TelemetrySample.TryCreate(
            session,
            position,
            counter,
            lap: null,
            lapDistance: null,
            onTrackState,
            observedAt);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(session, result.Value.Session);
        Assert.AreSame(position, result.Value.Position);
        Assert.AreSame(counter, result.Value.IncidentCounter);
        Assert.AreEqual(onTrackState, result.Value.OnTrackState);
        Assert.AreSame(observedAt, result.Value.ObservedAt);
        Assert.IsNull(result.Value.Lap);
        Assert.IsNull(result.Value.LapDistance);
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
        var lap = hasLap ? LapNumber.TryCreate(7).Value : null;
        var lapDistance = hasLapDistance ? LapDistance.TryCreate(0.625).Value : null;

        var result = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            TelemetryContractFixtures.CreateCounter(),
            lap,
            lapDistance,
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(lap, result.Value.Lap);
        Assert.AreSame(lapDistance, result.Value.LapDistance);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow("session")]
    [DataRow("position")]
    [DataRow("counter")]
    [DataRow("observed-at")]
    public void TryCreateRejectsEachMissingRequiredValueIndependently(string missingValue)
    {
        SimulatorSessionDescriptor? session = TelemetryContractFixtures.CreateSession();
        ReplayPosition? position = TelemetryContractFixtures.CreatePosition();
        IncidentCounter? counter = TelemetryContractFixtures.CreateCounter();
        UtcInstant? observedAt = TelemetryContractFixtures.CreateObservedAt();

        switch (missingValue)
        {
            case "session":
                session = null;
                break;
            case "position":
                position = null;
                break;
            case "counter":
                counter = null;
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
            counter,
            lap: null,
            lapDistance: null,
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
            TelemetryContractFixtures.CreateCounter(),
            lap: null,
            lapDistance: null,
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
            TelemetryContractFixtures.CreateCounter(),
            lap: null,
            lapDistance: null,
            (OnTrackState)int.MaxValue,
            TelemetryContractFixtures.CreateObservedAt());

        AssertInvalidSample(result);
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
}
