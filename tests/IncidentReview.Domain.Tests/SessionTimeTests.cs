using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class SessionTimeTests
{
    private const long MaximumWholeMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

    [TestMethod]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DifferentSessionTimesAreNotEqual() => Assert.AreNotEqual(
        SessionTime.TryCreateMilliseconds(0L).Value,
        SessionTime.TryCreateMilliseconds(1L).Value);

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(3_600_000L)]
    [DataRow(MaximumWholeMilliseconds)]
    public void IntegerMillisecondsRoundTripWithoutLoss(long milliseconds)
    {
        var sessionTime = SessionTime.TryCreateMilliseconds(milliseconds).Value;

        Assert.AreEqual(milliseconds, sessionTime.Milliseconds);
        Assert.AreEqual(milliseconds * TimeSpan.TicksPerMillisecond, sessionTime.Value.Ticks);
        Assert.AreEqual(
            sessionTime,
            SessionTime.TryCreate(sessionTime.Value).Value);
        Assert.AreEqual(
            milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sessionTime.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IntegerMillisecondsRejectNegativeAndOutOfDurationRange()
    {
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreateMilliseconds(-1L),
            "domain.session-time.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreateMilliseconds(long.MinValue),
            "domain.session-time.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreateMilliseconds(MaximumWholeMilliseconds + 1),
            "domain.session-time.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreateMilliseconds(long.MaxValue),
            "domain.session-time.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0d)]
    [DataRow(1d)]
    [DataRow(123_456d)]
    public void FiniteWholeNumericMillisecondsAreAccepted(double milliseconds)
    {
        var sessionTime = SessionTime.TryCreateMilliseconds(milliseconds).Value;

        Assert.AreEqual((long)milliseconds, sessionTime.Milliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void NumericMillisecondsRejectEveryInvalidCategory()
    {
        var invalidValues = new[]
        {
            double.NaN,
            double.NegativeInfinity,
            double.PositiveInfinity,
            -1d,
            0.5d,
            (double)MaximumWholeMilliseconds + 1_024,
        };

        foreach (var value in invalidValues)
        {
            DomainTestAssertions.IsValidationFailure(
                SessionTime.TryCreateMilliseconds(value),
                "domain.session-time.invalid");
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurationFactoryAcceptsZeroAndMaximumWholeMillisecond()
    {
        var zero = SessionTime.TryCreate(TimeSpan.Zero).Value;
        var maximum = SessionTime.TryCreate(
            TimeSpan.FromTicks(MaximumWholeMilliseconds * TimeSpan.TicksPerMillisecond)).Value;

        Assert.AreEqual(0L, zero.Milliseconds);
        Assert.AreEqual(MaximumWholeMilliseconds, maximum.Milliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurationFactoryRejectsNegativeAndSubMillisecondValues()
    {
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreate(TimeSpan.FromTicks(-1)),
            "domain.session-time.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreate(TimeSpan.FromTicks(1)),
            "domain.session-time.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionTime.TryCreate(TimeSpan.MaxValue),
            "domain.session-time.invalid");
    }
}
