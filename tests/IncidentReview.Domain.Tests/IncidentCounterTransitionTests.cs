using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentCounterTransitionTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EvaluateRejectsAMissingObservation()
    {
        var result = IncidentCounterTransition.Evaluate(null, null);

        DomainTestAssertions.IsValidationFailure(
            result,
            "domain.incident-transition.invalid-observation");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void MissingCheckpointEstablishesAnEpochZeroBaselineWithoutAnIncident()
    {
        var observation = IncidentTransitionTestData.Observation(
            counter: 4,
            replaySessionNumber: 2,
            sessionTimeMilliseconds: 12_345,
            observedAtUnixMilliseconds: 1_800_000_000_123,
            lap: 3,
            lapDistance: 0.75);

        var decision = AssertBaseline(
            IncidentCounterTransition.Evaluate(null, observation),
            IncidentBaselineReason.Initial);

        AssertCheckpointMatchesObservation(decision.NextCheckpoint, observation, expectedEpoch: 0);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SessionChangeEstablishesAnEpochZeroBaselineBeforeSessionNumberChecks()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(
            session: IncidentTransitionTestData.FirstSession,
            epoch: 9,
            counter: 12,
            replaySessionNumber: 1);
        var observation = IncidentTransitionTestData.Observation(
            session: IncidentTransitionTestData.SecondSession,
            counter: 2,
            replaySessionNumber: 7);

        var decision = AssertBaseline(
            IncidentCounterTransition.Evaluate(checkpoint, observation),
            IncidentBaselineReason.SessionChanged);

        AssertCheckpointMatchesObservation(decision.NextCheckpoint, observation, expectedEpoch: 0);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void RepeatedCounterReturnsTheSingletonNoWriteDecision()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(epoch: int.MaxValue, counter: 4);
        var observation = IncidentTransitionTestData.Observation(counter: 4);

        var first = IncidentCounterTransition.Evaluate(checkpoint, observation);
        var second = IncidentCounterTransition.Evaluate(checkpoint, observation);

        Assert.IsTrue(first.IsSuccess);
        Assert.AreSame(IncidentTransitionDecision.NoChange.Instance, first.Value);
        Assert.AreSame(first.Value, second.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CounterJumpRecordsOneCompleteIncreaseAndAtomicCheckpointMove()
    {
        var expectedCheckpoint = IncidentTransitionTestData.Checkpoint(
            epoch: 3,
            counter: 4,
            sessionTimeMilliseconds: 1_000,
            updatedAtUnixMilliseconds: 1_800_000_000_000);
        var observation = IncidentTransitionTestData.Observation(
            counter: 9,
            sessionTimeMilliseconds: 1_100,
            observedAtUnixMilliseconds: 1_800_000_000_100,
            lap: 5,
            lapDistance: 0.2);

        var result = IncidentCounterTransition.Evaluate(expectedCheckpoint, observation);

        Assert.IsTrue(result.IsSuccess);
        var decision = Assert.IsInstanceOfType<IncidentTransitionDecision.RecordIncrease>(result.Value);
        Assert.AreEqual(9, decision.Points.Total);
        Assert.AreEqual(5, decision.Points.Delta);
        Assert.AreSame(expectedCheckpoint, decision.ExpectedCheckpoint);
        Assert.AreSame(observation, decision.Observation);
        AssertCheckpointMatchesObservation(decision.NextCheckpoint, observation, expectedEpoch: 3);
        Assert.AreSame(expectedCheckpoint.CounterEpoch, decision.NextCheckpoint.CounterEpoch);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CounterDecreaseEstablishesANextEpochBaselineWithoutAnIncident()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(epoch: 7, counter: 12);
        var observation = IncidentTransitionTestData.Observation(counter: 2);

        var decision = AssertBaseline(
            IncidentCounterTransition.Evaluate(checkpoint, observation),
            IncidentBaselineReason.CounterReset);

        AssertCheckpointMatchesObservation(decision.NextCheckpoint, observation, expectedEpoch: 8);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CounterResetAtMaximumEpochReturnsConflictInsteadOfWrappingOrThrowing()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(epoch: int.MaxValue, counter: 4);
        var observation = IncidentTransitionTestData.Observation(counter: 3);

        AssertConflict(
            IncidentCounterTransition.Evaluate(checkpoint, observation),
            "domain.incident-transition.counter-epoch-overflow");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IncreaseAtMaximumEpochRemainsValidBecauseItDoesNotAdvanceTheEpoch()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(epoch: int.MaxValue, counter: 4);
        var observation = IncidentTransitionTestData.Observation(counter: 5);

        var result = IncidentCounterTransition.Evaluate(checkpoint, observation);

        Assert.IsTrue(result.IsSuccess);
        var decision = Assert.IsInstanceOfType<IncidentTransitionDecision.RecordIncrease>(result.Value);
        Assert.AreEqual(int.MaxValue, decision.NextCheckpoint.CounterEpoch.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void SameSessionReplaySessionMismatchAlwaysReturnsConflict(int observedCounter)
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(
            counter: 4,
            replaySessionNumber: 1);
        var observation = IncidentTransitionTestData.Observation(
            counter: observedCounter,
            replaySessionNumber: 2);

        AssertConflict(
            IncidentCounterTransition.Evaluate(checkpoint, observation),
            "domain.incident-transition.session-number-mismatch");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EvaluationIsDeterministicAndAllocatesNoIdentityInItsDecision()
    {
        var checkpoint = IncidentTransitionTestData.Checkpoint(epoch: 2, counter: 1);
        var observation = IncidentTransitionTestData.Observation(counter: 4);

        var first = IncidentCounterTransition.Evaluate(checkpoint, observation).Value;
        var second = IncidentCounterTransition.Evaluate(checkpoint, observation).Value;

        Assert.AreEqual(first, second);
        Assert.IsFalse(
            typeof(IncidentTransitionDecision.RecordIncrease)
                .GetProperties()
                .Any(static property => property.PropertyType == typeof(IncidentId)));
    }

    private static IncidentTransitionDecision.EstablishBaseline AssertBaseline(
        Result<IncidentTransitionDecision> result,
        IncidentBaselineReason expectedReason)
    {
        Assert.IsTrue(result.IsSuccess);
        var decision = Assert.IsInstanceOfType<IncidentTransitionDecision.EstablishBaseline>(
            result.Value);
        Assert.AreSame(expectedReason, decision.Reason);
        return decision;
    }

    private static void AssertCheckpointMatchesObservation(
        IncidentCheckpoint checkpoint,
        IncidentObservation observation,
        int expectedEpoch)
    {
        Assert.AreSame(observation.Session, checkpoint.Session);
        Assert.AreEqual(expectedEpoch, checkpoint.CounterEpoch.Value);
        Assert.AreSame(observation.IncidentCounter, checkpoint.LastCounter);
        Assert.AreSame(observation.Position, checkpoint.LastPosition);
        Assert.AreSame(observation.ObservedAt, checkpoint.UpdatedAt);
    }

    private static void AssertConflict(
        Result<IncidentTransitionDecision> result,
        string expectedCode)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(ErrorKind.Conflict, result.Error.Kind);
        Assert.AreEqual(expectedCode, result.Error.Code.Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
    }
}
