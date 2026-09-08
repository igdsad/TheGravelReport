using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentTransitionDecisionTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void BaselineReasonsAreClosedStableValues()
    {
        Assert.AreEqual("Initial", IncidentBaselineReason.Initial.ToString());
        Assert.AreEqual("SessionChanged", IncidentBaselineReason.SessionChanged.ToString());
        Assert.AreEqual("HeatChanged", IncidentBaselineReason.HeatChanged.ToString());
        Assert.AreEqual("CounterReset", IncidentBaselineReason.CounterReset.ToString());
        Assert.AreNotEqual(IncidentBaselineReason.Initial, IncidentBaselineReason.CounterReset);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void BaselineDecisionEqualityIncludesReasonAndNextCheckpoint()
    {
        var initialObservation = IncidentTransitionTestData.Observation(counter: 1);
        var equalFirst = AssertBaseline(
            IncidentCounterTransition.Evaluate(null, initialObservation));
        var equalSecond = AssertBaseline(
            IncidentCounterTransition.Evaluate(null, initialObservation));
        var differentReason = AssertBaseline(
            IncidentCounterTransition.Evaluate(
                IncidentTransitionTestData.Checkpoint(
                    session: IncidentTransitionTestData.SecondSession,
                    counter: 1),
                initialObservation));
        var differentCheckpoint = AssertBaseline(
            IncidentCounterTransition.Evaluate(
                null,
                IncidentTransitionTestData.Observation(counter: 2)));

        Assert.IsTrue(equalFirst.Equals(equalSecond));
        Assert.IsTrue(equalFirst.Equals((object)equalSecond));
        Assert.AreEqual(equalFirst, equalSecond);
        Assert.AreEqual(equalFirst.GetHashCode(), equalSecond.GetHashCode());
        Assert.IsFalse(equalFirst.Equals(null));
        Assert.IsFalse(equalFirst.Equals(new object()));
        Assert.AreNotEqual(equalFirst, differentReason);
        Assert.AreNotEqual(equalFirst, differentCheckpoint);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IncreaseDecisionEqualityIncludesEveryAtomicTransitionComponent()
    {
        var expected = IncidentTransitionTestData.Checkpoint(counter: 1);
        var observation = IncidentTransitionTestData.Observation(counter: 4);
        var equalFirst = AssertIncrease(IncidentCounterTransition.Evaluate(expected, observation));
        var equalSecond = AssertIncrease(IncidentCounterTransition.Evaluate(expected, observation));

        var differentPoints = AssertIncrease(IncidentCounterTransition.Evaluate(
            expected,
            IncidentTransitionTestData.Observation(counter: 5)));
        var differentExpected = AssertIncrease(IncidentCounterTransition.Evaluate(
            IncidentTransitionTestData.Checkpoint(counter: 1, sessionTimeMilliseconds: 999),
            observation));
        var differentNext = AssertIncrease(IncidentCounterTransition.Evaluate(
            expected,
            IncidentTransitionTestData.Observation(
                counter: 4,
                observedAtUnixMilliseconds: 1_800_000_000_001)));
        var differentObservation = AssertIncrease(IncidentCounterTransition.Evaluate(
            expected,
            IncidentTransitionTestData.Observation(counter: 4, lap: 2)));

        Assert.IsTrue(equalFirst.Equals(equalSecond));
        Assert.IsTrue(equalFirst.Equals((object)equalSecond));
        Assert.AreEqual(equalFirst, equalSecond);
        Assert.AreEqual(equalFirst.GetHashCode(), equalSecond.GetHashCode());
        Assert.IsFalse(equalFirst.Equals(null));
        Assert.IsFalse(equalFirst.Equals(new object()));
        Assert.AreNotEqual(equalFirst, differentPoints);
        Assert.AreNotEqual(equalFirst, differentExpected);
        Assert.AreNotEqual(equalFirst, differentNext);
        Assert.AreNotEqual(equalFirst, differentObservation);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DecisionHierarchyIsClosedAndConcreteDecisionsAreImmutable()
    {
        Assert.IsTrue(typeof(IncidentTransitionDecision).IsAbstract);
        Assert.HasCount(0, typeof(IncidentTransitionDecision).GetConstructors());

        var concreteTypes = new[]
        {
            typeof(IncidentTransitionDecision.NoChange),
            typeof(IncidentTransitionDecision.EstablishBaseline),
            typeof(IncidentTransitionDecision.RecordIncrease),
        };

        foreach (var type in concreteTypes)
        {
            Assert.IsTrue(type.IsSealed);
            Assert.HasCount(0, type.GetConstructors());
            Assert.IsTrue(type.GetProperties().All(static property => property.SetMethod is null));
        }
    }

    private static IncidentTransitionDecision.EstablishBaseline AssertBaseline(
        IncidentReview.Results.Result<IncidentTransitionDecision> result) =>
        Assert.IsInstanceOfType<IncidentTransitionDecision.EstablishBaseline>(result.Value);

    private static IncidentTransitionDecision.RecordIncrease AssertIncrease(
        IncidentReview.Results.Result<IncidentTransitionDecision> result) =>
        Assert.IsInstanceOfType<IncidentTransitionDecision.RecordIncrease>(result.Value);
}
