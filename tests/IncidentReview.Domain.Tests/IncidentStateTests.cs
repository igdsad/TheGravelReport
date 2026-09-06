using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentStateTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ReviewStatusesHaveExplicitStableCodesAndRoundTrip()
    {
        AssertStableStatus(IncidentReviewStatus.Pending, 1, "Pending");
        AssertStableStatus(IncidentReviewStatus.Reviewed, 2, "Reviewed");
        AssertStableStatus(IncidentReviewStatus.Dismissed, 3, "Dismissed");
        Assert.AreNotEqual(IncidentReviewStatus.Pending, IncidentReviewStatus.Reviewed);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(int.MinValue)]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(int.MaxValue)]
    public void ReviewStatusRejectsUnknownPersistedCodes(int value) =>
        DomainTestAssertions.IsValidationFailure(
            IncidentReviewStatus.TryCreate(value),
            "domain.incident-review-status.invalid");

    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ClassificationsHaveExplicitStableCodesAndRoundTrip()
    {
        AssertStableClassification(IncidentClassification.OffTrack, 1, "OffTrack");
        AssertStableClassification(IncidentClassification.LossOfControl, 2, "LossOfControl");
        AssertStableClassification(IncidentClassification.Contact, 3, "Contact");
        AssertStableClassification(IncidentClassification.UnsafeRejoin, 4, "UnsafeRejoin");
        AssertStableClassification(IncidentClassification.Other, 5, "Other");
        Assert.AreNotEqual(IncidentClassification.OffTrack, IncidentClassification.Other);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(int.MinValue)]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(int.MaxValue)]
    public void ClassificationRejectsUnknownPersistedCodes(int value) =>
        DomainTestAssertions.IsValidationFailure(
            IncidentClassification.TryCreate(value),
            "domain.incident-classification.invalid");

    private static void AssertStableStatus(
        IncidentReviewStatus expected,
        int code,
        string name)
    {
        var result = IncidentReviewStatus.TryCreate(code);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(expected, result.Value);
        Assert.AreEqual(code, result.Value.Value);
        Assert.AreEqual(name, result.Value.ToString());
    }

    private static void AssertStableClassification(
        IncidentClassification expected,
        int code,
        string name)
    {
        var result = IncidentClassification.TryCreate(code);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(expected, result.Value);
        Assert.AreEqual(code, result.Value.Value);
        Assert.AreEqual(name, result.Value.ToString());
    }
}
