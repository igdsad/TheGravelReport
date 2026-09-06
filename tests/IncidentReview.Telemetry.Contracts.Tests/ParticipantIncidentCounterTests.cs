namespace IncidentReview.Telemetry.Contracts.Tests;

[TestClass]
public sealed class ParticipantIncidentCounterTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreatePreservesParticipantCounterAndOptionalTrackContext()
    {
        var participant = TelemetryContractFixtures.CreateParticipant();
        var counter = IncidentCounter.TryCreate(8).Value;
        var lap = LapNumber.TryCreate(3).Value;
        var lapDistance = LapDistance.TryCreate(0.75).Value;

        var first = ParticipantIncidentCounter.TryCreate(
            participant,
            counter,
            lap,
            lapDistance).Value;
        var second = ParticipantIncidentCounter.TryCreate(
            participant,
            counter,
            lap,
            lapDistance).Value;

        Assert.AreSame(participant, first.Participant);
        Assert.AreSame(counter, first.IncidentCounter);
        Assert.AreSame(lap, first.Lap);
        Assert.AreSame(lapDistance, first.LapDistance);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreateRejectsEachMissingRequiredValueIndependently()
    {
        var participant = TelemetryContractFixtures.CreateParticipant();
        var counter = IncidentCounter.TryCreate(0).Value;

        AssertInvalid(null, counter);
        AssertInvalid(participant, null);
        AssertInvalid(null, null);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void CounterCannotBeConstructedOrMutatedIntoAnInvalidState()
    {
        Assert.IsTrue(typeof(ParticipantIncidentCounter).IsSealed);
        Assert.IsEmpty(typeof(ParticipantIncidentCounter).GetConstructors());
        Assert.IsTrue(typeof(ParticipantIncidentCounter).GetProperties().All(property =>
            property.SetMethod is null));
    }

    private static void AssertInvalid(
        IncidentParticipant? participant,
        IncidentCounter? counter)
    {
        var result = ParticipantIncidentCounter.TryCreate(
            participant,
            counter,
            lap: null,
            lapDistance: null);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TelemetryErrorCodes.InvalidSample, result.Error!.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
    }
}
