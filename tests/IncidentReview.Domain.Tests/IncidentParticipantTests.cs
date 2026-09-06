using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentParticipantTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ParticipantIdentityPreservesOpaqueBoundedTextWithValueEquality()
    {
        var text = " iracing:car-index:12 ";

        var first = ParticipantIdentity.TryCreate(text).Value;
        var second = ParticipantIdentity.TryCreate(text).Value;

        Assert.AreEqual(text, first.Value);
        Assert.AreEqual(text, first.ToString());
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(ParticipantIdentity.TryCreate(
            new string('p', ParticipantIdentity.MaximumLength)).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ParticipantIdentityRejectsMissingOversizedOrMalformedText()
    {
        AssertInvalidIdentity(null);
        AssertInvalidIdentity(string.Empty);
        AssertInvalidIdentity("   ");
        AssertInvalidIdentity(new string('p', ParticipantIdentity.MaximumLength + 1));
        AssertInvalidIdentity("participant\n1");
        AssertInvalidIdentity("participant\uD800");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IncidentParticipantNormalizesOptionalDisplayContext()
    {
        var identity = ParticipantIdentity.TryCreate("car:12").Value;

        var result = IncidentParticipant.TryCreate(
            identity,
            "  Rene\u0301 Gravel  ",
            "  Gravel Team  ",
            "  012  ");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(identity, result.Value.Identity);
        Assert.AreEqual("René Gravel", result.Value.DriverName);
        Assert.AreEqual("Gravel Team", result.Value.TeamName);
        Assert.AreEqual("012", result.Value.CarNumber);

        var empty = IncidentParticipant.TryCreate(identity, null, "   ", string.Empty).Value;
        Assert.IsNull(empty.DriverName);
        Assert.IsNull(empty.TeamName);
        Assert.IsNull(empty.CarNumber);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IncidentParticipantIndependentlyRejectsInvalidRequiredOrDisplayValues()
    {
        var identity = ParticipantIdentity.TryCreate("car:12").Value;
        var oversized = new string('x', IncidentParticipant.MaximumDisplayTextLength + 1);

        AssertInvalidParticipant(null, null, null, null);
        AssertInvalidParticipant(identity, oversized, null, null);
        AssertInvalidParticipant(identity, null, oversized, null);
        AssertInvalidParticipant(identity, null, null, oversized);
        AssertInvalidParticipant(identity, "driver\nname", null, null);
        AssertInvalidParticipant(identity, null, "team\uD800", null);
    }

    private static void AssertInvalidIdentity(string? value) =>
        DomainTestAssertions.IsValidationFailure(
            ParticipantIdentity.TryCreate(value),
            "domain.participant-identity.invalid");

    private static void AssertInvalidParticipant(
        ParticipantIdentity? identity,
        string? driverName,
        string? teamName,
        string? carNumber) => DomainTestAssertions.IsValidationFailure(
            IncidentParticipant.TryCreate(identity, driverName, teamName, carNumber),
            "domain.incident-participant.invalid");
}
