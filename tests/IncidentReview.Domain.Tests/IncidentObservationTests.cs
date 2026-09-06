using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentObservationTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ObservationPreservesRequiredAndOptionalContextWithValueEquality()
    {
        var session = IncidentTransitionTestData.FirstSession;
        var participant = IncidentTransitionTestData.FirstParticipant;
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(2).Value,
            SessionTime.TryCreateMilliseconds(12_345L).Value).Value;
        var counter = IncidentCounter.TryCreate(4).Value;
        var lap = LapNumber.TryCreate(3).Value;
        var lapDistance = LapDistance.TryCreate(0.75).Value;
        var observedAt = UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_123).Value;

        var first = IncidentObservation.TryCreate(
            session,
            participant,
            position,
            counter,
            lap,
            lapDistance,
            observedAt).Value;
        var second = IncidentObservation.TryCreate(
            session,
            participant,
            position,
            counter,
            lap,
            lapDistance,
            observedAt).Value;

        Assert.AreSame(session, first.Session);
        Assert.AreSame(participant, first.Participant);
        Assert.AreSame(position, first.Position);
        Assert.AreSame(counter, first.IncidentCounter);
        Assert.AreSame(lap, first.Lap);
        Assert.AreSame(lapDistance, first.LapDistance);
        Assert.AreSame(observedAt, first.ObservedAt);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ObservationRepresentsUnavailableLapContextAsNull()
    {
        var observation = IncidentTransitionTestData.Observation(lap: null, lapDistance: null);

        Assert.IsNull(observation.Lap);
        Assert.IsNull(observation.LapDistance);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ObservationIndependentlyRejectsEveryMissingRequiredValue()
    {
        var session = IncidentTransitionTestData.FirstSession;
        var participant = IncidentTransitionTestData.FirstParticipant;
        var position = IncidentTransitionTestData.Observation().Position;
        var counter = IncidentCounter.TryCreate(0).Value;
        var observedAt = UtcInstant.TryCreateUnixMilliseconds(0).Value;

        AssertInvalid(null, participant, position, counter, observedAt);
        AssertInvalid(session, null, position, counter, observedAt);
        AssertInvalid(session, participant, null, counter, observedAt);
        AssertInvalid(session, participant, position, null, observedAt);
        AssertInvalid(session, participant, position, counter, null);
        AssertInvalid(null, null, null, null, null);
    }

    private static void AssertInvalid(
        SessionIdentity? session,
        IncidentParticipant? participant,
        ReplayPosition? position,
        IncidentCounter? counter,
        UtcInstant? observedAt) => DomainTestAssertions.IsValidationFailure(
            IncidentObservation.TryCreate(
                session,
                participant,
                position,
                counter,
                lap: null,
                lapDistance: null,
                observedAt),
            "domain.incident-observation.invalid");
}
