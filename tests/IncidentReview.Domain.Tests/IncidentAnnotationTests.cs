using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class IncidentAnnotationTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AnnotationNormalizesNotesAndPreservesClassification()
    {
        var result = IncidentAnnotation.TryCreate(
            "  Cafe\u0301\r\nLine 2\t  ",
            IncidentClassification.Contact);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Caf\u00e9\nLine 2", result.Value.Notes);
        Assert.AreSame(IncidentClassification.Contact, result.Value.Classification);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t\r\n ")]
    public void AnnotationRepresentsMissingOrBlankNotesAsNull(string? notes)
    {
        var annotation = IncidentAnnotation.TryCreate(notes, null).Value;

        Assert.IsNull(annotation.Notes);
        Assert.IsNull(annotation.Classification);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AnnotationAcceptsTheExactNotesLimitAndValidSurrogatePairs()
    {
        var exactLimit = new string('a', IncidentAnnotation.MaximumNotesLength);
        var withEmoji = "Turn one \ud83c\udfc1";

        Assert.AreEqual(
            exactLimit,
            IncidentAnnotation.TryCreate(exactLimit, null).Value.Notes);
        Assert.AreEqual(
            withEmoji,
            IncidentAnnotation.TryCreate(withEmoji, null).Value.Notes);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AnnotationRejectsEveryValueOverTheNormalizedLengthLimit()
    {
        AssertInvalidNotes(new string('a', IncidentAnnotation.MaximumNotesLength + 1));
        AssertInvalidNotes(new string('a', (IncidentAnnotation.MaximumNotesLength * 2) + 1));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AnnotationRejectsDisallowedControlsAndMalformedUtf16()
    {
        AssertInvalidNotes("a\0b");
        AssertInvalidNotes("a\u000bb");
        AssertInvalidNotes("a\ud800");
        AssertInvalidNotes("a\ud800b");
        AssertInvalidNotes("a\udc00b");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void AnnotationNormalizesStandaloneCarriageReturnsToLineFeeds()
    {
        var annotation = IncidentAnnotation.TryCreate("Line 1\rLine 2", null).Value;

        Assert.AreEqual("Line 1\nLine 2", annotation.Notes);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EqualNormalizedAnnotationsHaveValueEquality()
    {
        var first = IncidentAnnotation.TryCreate(" Caf\u00e9 ", IncidentClassification.Other).Value;
        var second = IncidentAnnotation.TryCreate("Cafe\u0301", IncidentClassification.Other).Value;
        var different = IncidentAnnotation.TryCreate("Different", IncidentClassification.Other).Value;

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, different);
    }

    private static void AssertInvalidNotes(string notes) =>
        DomainTestAssertions.IsValidationFailure(
            IncidentAnnotation.TryCreate(notes, null),
            "domain.incident-annotation.invalid-notes");
}
