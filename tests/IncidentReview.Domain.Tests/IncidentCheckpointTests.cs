using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentCheckpointTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CheckpointPreservesDurableProgressWithValueEquality()
    {
        var session = IncidentTransitionTestData.FirstSession;
        var epoch = CounterEpoch.TryCreate(2).Value;
        var counter = IncidentCounter.TryCreate(8).Value;
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(1).Value,
            SessionTime.TryCreateMilliseconds(5_000L).Value).Value;
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_000).Value;

        var first = IncidentCheckpoint.TryCreate(
            session,
            epoch,
            counter,
            position,
            updatedAt).Value;
        var second = IncidentCheckpoint.TryCreate(
            session,
            epoch,
            counter,
            position,
            updatedAt).Value;

        Assert.AreSame(session, first.Session);
        Assert.AreSame(epoch, first.CounterEpoch);
        Assert.AreSame(counter, first.LastCounter);
        Assert.AreSame(position, first.LastPosition);
        Assert.AreSame(updatedAt, first.UpdatedAt);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CheckpointIndependentlyRejectsEveryMissingRequiredValue()
    {
        var session = IncidentTransitionTestData.FirstSession;
        var epoch = CounterEpoch.TryCreate(0).Value;
        var counter = IncidentCounter.TryCreate(0).Value;
        var position = IncidentTransitionTestData.Observation().Position;
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(0).Value;

        AssertInvalid(null, epoch, counter, position, updatedAt);
        AssertInvalid(session, null, counter, position, updatedAt);
        AssertInvalid(session, epoch, null, position, updatedAt);
        AssertInvalid(session, epoch, counter, null, updatedAt);
        AssertInvalid(session, epoch, counter, position, null);
        AssertInvalid(null, null, null, null, null);
    }

    private static void AssertInvalid(
        SessionIdentity? session,
        CounterEpoch? epoch,
        IncidentCounter? counter,
        ReplayPosition? position,
        UtcInstant? updatedAt) => DomainTestAssertions.IsValidationFailure(
            IncidentCheckpoint.TryCreate(session, epoch, counter, position, updatedAt),
            "domain.incident-checkpoint.invalid");
}
