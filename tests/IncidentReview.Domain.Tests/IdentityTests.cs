using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IdentityTests
{
    private const string CanonicalUuid7 = "018f0c24-7a35-7a0d-8000-000000000001";
    private const string OtherCanonicalUuid7 = "018f0c24-7a35-7a0d-8000-000000000002";

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void GeneratedSessionIdentityIsAValidCanonicalUuid7()
    {
        var identity = SessionIdentity.Generate();

        Assert.AreEqual(7, identity.Value.Version);
        Assert.IsTrue(identity.Value.Variant is >= 8 and <= 11);
        Assert.AreEqual(identity.Value.ToString("D"), identity.ToString());
        Assert.AreEqual(identity, SessionIdentity.TryParse(identity.ToString()).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void GeneratedIncidentIdIsAValidCanonicalUuid7()
    {
        var incidentId = IncidentId.Generate();

        Assert.AreEqual(7, incidentId.Value.Version);
        Assert.IsTrue(incidentId.Value.Variant is >= 8 and <= 11);
        Assert.AreEqual(incidentId.Value.ToString("D"), incidentId.ToString());
        Assert.AreEqual(incidentId, IncidentId.TryParse(incidentId.ToString()).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SessionIdentityRehydratesWithValueEquality()
    {
        var value = Guid.ParseExact(CanonicalUuid7, "D");

        var fromGuid = SessionIdentity.TryCreate(value).Value;
        var fromText = SessionIdentity.TryParse(CanonicalUuid7).Value;

        Assert.AreEqual(fromGuid, fromText);
        Assert.AreEqual(fromGuid.GetHashCode(), fromText.GetHashCode());
        Assert.AreEqual(value, fromText.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IncidentIdRehydratesWithValueEquality()
    {
        var value = Guid.ParseExact(CanonicalUuid7, "D");

        var fromGuid = IncidentId.TryCreate(value).Value;
        var fromText = IncidentId.TryParse(CanonicalUuid7).Value;

        Assert.AreEqual(fromGuid, fromText);
        Assert.AreEqual(fromGuid.GetHashCode(), fromText.GetHashCode());
        Assert.AreEqual(value, fromText.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IdentitiesWithDifferentValuesAreNotEqual()
    {
        Assert.AreNotEqual(
            SessionIdentity.TryParse(CanonicalUuid7).Value,
            SessionIdentity.TryParse(OtherCanonicalUuid7).Value);
        Assert.AreNotEqual(
            IncidentId.TryParse(CanonicalUuid7).Value,
            IncidentId.TryParse(OtherCanonicalUuid7).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IdentityGuidFactoriesRejectEmptyNonVersion7AndNonRfcVariants()
    {
        var nonRfcVersion7 = Guid.ParseExact(
            "018f0c24-7a35-7a0d-0000-000000000001",
            "D");

        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryCreate(Guid.Empty),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryCreate(Guid.NewGuid()),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryCreate(nonRfcVersion7),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryCreate(Guid.Empty),
            "domain.incident-id.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryCreate(Guid.NewGuid()),
            "domain.incident-id.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryCreate(nonRfcVersion7),
            "domain.incident-id.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-uuid")]
    [DataRow("018F0C24-7A35-7A0D-8000-000000000001")]
    [DataRow("{018f0c24-7a35-7a0d-8000-000000000001}")]
    [DataRow(" 018f0c24-7a35-7a0d-8000-000000000001")]
    [DataRow("018f0c24-7a35-4a0d-8000-000000000001")]
    [DataRow("018f0c24-7a35-7a0d-0000-000000000001")]
    public void IdentityTextFactoriesRejectNonCanonicalOrInvalidValues(string? text)
    {
        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryParse(text),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryParse(text),
            "domain.incident-id.invalid");
    }
}
