using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentPointsAndEpochTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(1, 1)]
    [DataRow(4, 1)]
    [DataRow(4, 4)]
    [DataRow(int.MaxValue, 1)]
    [DataRow(int.MaxValue, int.MaxValue)]
    public void IncidentPointsAcceptValidBoundaryCombinations(int total, int delta)
    {
        var points = IncidentPoints.TryCreate(total, delta).Value;

        Assert.AreEqual(total, points.Total);
        Assert.AreEqual(delta, points.Delta);
        Assert.AreEqual(points, IncidentPoints.TryCreate(total, delta).Value);
        Assert.AreEqual(points.GetHashCode(), IncidentPoints.TryCreate(total, delta).Value.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(-1, 1)]
    [DataRow(int.MinValue, 1)]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    [DataRow(1, -1)]
    [DataRow(1, 2)]
    public void IncidentPointsRejectEveryInvalidInvariantCombination(int total, int delta) =>
        DomainTestAssertions.IsValidationFailure(
            IncidentPoints.TryCreate(total, delta),
            "domain.incident-points.invalid");

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void CounterEpochAcceptsAndRoundTripsItsFullRange(int value)
    {
        var epoch = CounterEpoch.TryCreate(value).Value;

        Assert.AreEqual(value, epoch.Value);
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), epoch.ToString());
        Assert.AreEqual(epoch, CounterEpoch.TryCreate(value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void CounterEpochRejectsNegativeValues(int value) =>
        DomainTestAssertions.IsValidationFailure(
            CounterEpoch.TryCreate(value),
            "domain.counter-epoch.invalid");
}
