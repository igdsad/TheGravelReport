using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class ReplayPositionAndLapDistanceTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ReplayPositionCombinesValidatedValuesWithValueEquality()
    {
        var sessionNumber = SessionNumber.TryCreate(2).Value;
        var sessionTime = SessionTime.TryCreateMilliseconds(42_123L).Value;

        var first = ReplayPosition.TryCreate(sessionNumber, sessionTime).Value;
        var second = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(2).Value,
            SessionTime.TryCreateMilliseconds(42_123L).Value).Value;

        Assert.AreSame(sessionNumber, first.SessionNumber);
        Assert.AreSame(sessionTime, first.SessionTime);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(
            first,
            ReplayPosition.TryCreate(
                sessionNumber,
                SessionTime.TryCreateMilliseconds(42_124L).Value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ReplayPositionIndependentlyRejectsEveryMissingComponentCombination()
    {
        var sessionNumber = SessionNumber.TryCreate(0).Value;
        var sessionTime = SessionTime.TryCreateMilliseconds(0L).Value;

        DomainTestAssertions.IsValidationFailure(
            ReplayPosition.TryCreate(null, sessionTime),
            "domain.replay-position.invalid");
        DomainTestAssertions.IsValidationFailure(
            ReplayPosition.TryCreate(sessionNumber, null),
            "domain.replay-position.invalid");
        DomainTestAssertions.IsValidationFailure(
            ReplayPosition.TryCreate(null, null),
            "domain.replay-position.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0d)]
    [DataRow(0.25d)]
    [DataRow(0.5d)]
    [DataRow(1d)]
    public void LapDistanceAcceptsInclusiveNormalizedBoundaries(double value)
    {
        var distance = LapDistance.TryCreate(value).Value;

        Assert.AreEqual(value, distance.Value);
        Assert.AreEqual(distance, LapDistance.TryCreate(value).Value);
        Assert.AreEqual(
            value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            distance.ToString());

        if (value != 1d)
        {
            Assert.AreNotEqual(distance, LapDistance.TryCreate(1d).Value);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void LapDistanceRejectsEveryNonFiniteAndOutOfRangeCategory()
    {
        var invalidValues = new[]
        {
            double.NaN,
            double.NegativeInfinity,
            double.PositiveInfinity,
            double.BitDecrement(0d),
            double.BitIncrement(1d),
        };

        foreach (var value in invalidValues)
        {
            DomainTestAssertions.IsValidationFailure(
                LapDistance.TryCreate(value),
                "domain.lap-distance.invalid");
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void LapDistanceCanonicalizesNegativeZero()
    {
        var distance = LapDistance.TryCreate(-0d).Value;

        Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(distance.Value));
        Assert.AreEqual("0", distance.ToString());
    }
}
