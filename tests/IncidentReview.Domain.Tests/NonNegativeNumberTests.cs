using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class NonNegativeNumberTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-TST-001")]
    public void NonNegativeValuesUseTheirNumericStateForEquality()
    {
        Assert.AreNotEqual(SessionNumber.TryCreate(0).Value, SessionNumber.TryCreate(1).Value);
        Assert.AreNotEqual(IncidentCounter.TryCreate(0).Value, IncidentCounter.TryCreate(1).Value);
        Assert.AreNotEqual(LapNumber.TryCreate(0).Value, LapNumber.TryCreate(1).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void SessionNumberAcceptsTheFullNonNegativeIntegerRange(int value)
    {
        var number = SessionNumber.TryCreate(value).Value;

        Assert.AreEqual(value, number.Value);
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), number.ToString());
        Assert.AreEqual(number, SessionNumber.TryCreate(value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void SessionNumberRejectsNegativeValues(int value) =>
        DomainTestAssertions.IsValidationFailure(
            SessionNumber.TryCreate(value),
            "domain.session-number.invalid");

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void IncidentCounterAcceptsTheFullNonNegativeIntegerRange(int value)
    {
        var counter = IncidentCounter.TryCreate(value).Value;

        Assert.AreEqual(value, counter.Value);
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), counter.ToString());
        Assert.AreEqual(counter, IncidentCounter.TryCreate(value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void IncidentCounterRejectsNegativeValues(int value) =>
        DomainTestAssertions.IsValidationFailure(
            IncidentCounter.TryCreate(value),
            "domain.incident-counter.invalid");

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(int.MaxValue)]
    public void LapNumberAcceptsTheFullNonNegativeIntegerRange(int value)
    {
        var lap = LapNumber.TryCreate(value).Value;

        Assert.AreEqual(value, lap.Value);
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), lap.ToString());
        Assert.AreEqual(lap, LapNumber.TryCreate(value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void LapNumberRejectsNegativeValues(int value) =>
        DomainTestAssertions.IsValidationFailure(
            LapNumber.TryCreate(value),
            "domain.lap-number.invalid");
}
