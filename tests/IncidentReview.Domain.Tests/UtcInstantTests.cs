using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class UtcInstantTests
{
    private const long MinimumUnixMilliseconds = -62_135_596_800_000;
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(MinimumUnixMilliseconds)]
    [DataRow(-1L)]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(MaximumUnixMilliseconds)]
    public void UnixMillisecondsRoundTripAcrossTheSupportedRange(long milliseconds)
    {
        var instant = UtcInstant.TryCreateUnixMilliseconds(milliseconds).Value;

        Assert.AreEqual(milliseconds, instant.UnixMilliseconds);
        Assert.AreEqual(TimeSpan.Zero, instant.Value.Offset);
        Assert.AreEqual(
            instant,
            UtcInstant.TryCreate(instant.Value).Value);
        Assert.AreEqual(
            instant,
            UtcInstant.TryCreate(instant.Value.UtcDateTime).Value);
        StringAssert.EndsWith(instant.ToString(), "+00:00");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void UnixMillisecondsRejectValuesOutsideTheDateTimeOffsetRange()
    {
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreateUnixMilliseconds(MinimumUnixMilliseconds - 1),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreateUnixMilliseconds(MaximumUnixMilliseconds + 1),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreateUnixMilliseconds(long.MinValue),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreateUnixMilliseconds(long.MaxValue),
            "domain.utc-instant.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TimestampFactoryRejectsNonUtcAndSubMillisecondValues()
    {
        var nonUtcOffset = new DateTimeOffset(
            2026,
            9,
            6,
            1,
            2,
            3,
            TimeSpan.FromHours(-4));
        var subMillisecondUtc = DateTimeOffset.UnixEpoch.AddTicks(1);

        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreate(nonUtcOffset),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreate(subMillisecondUtc),
            "domain.utc-instant.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DateTimeFactoryRejectsLocalUnspecifiedAndSubMillisecondValues()
    {
        var wholeMillisecond = DateTime.UnixEpoch;

        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreate(DateTime.SpecifyKind(wholeMillisecond, DateTimeKind.Local)),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreate(DateTime.SpecifyKind(wholeMillisecond, DateTimeKind.Unspecified)),
            "domain.utc-instant.invalid");
        DomainTestAssertions.IsValidationFailure(
            UtcInstant.TryCreate(wholeMillisecond.AddTicks(1)),
            "domain.utc-instant.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EqualInstantsHaveValueEqualityAndEqualHashCodes()
    {
        var first = UtcInstant.TryCreateUnixMilliseconds(1_799_999_999_123).Value;
        var second = UtcInstant.TryCreate(first.Value).Value;

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, UtcInstant.TryCreateUnixMilliseconds(first.UnixMilliseconds + 1).Value);
    }
}
