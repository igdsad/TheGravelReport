using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class CustomEventValueTests
{
    private const string ExpectedSessionId = "bf937652-2637-559b-9e6d-91ba750e19a8";
    private const string ExpectedCustomEventId = "3e0943f8-9f09-5d27-8379-0d865604c0ec";

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void CustomEventIdHasAStableGoldenVector()
    {
        var session = CreateSession("v1:subsession:123456:session:0");
        var position = CreatePosition(sessionNumber: 0, milliseconds: 123_456);

        var first = CustomEventId.CreateDeterministic(session, position);
        var second = CustomEventId.CreateDeterministic(session, position);

        Assert.AreEqual(ExpectedSessionId, session.ToString());
        Assert.AreEqual(ExpectedCustomEventId, first.ToString());
        Assert.AreEqual(first, second);
        Assert.AreEqual(5, first.Value.Version);
        Assert.IsTrue(first.Value.Variant is >= 8 and <= 11);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EveryIdentityComponentCanIndependentlyChangeTheCustomEventId()
    {
        var session = CreateSession("v1:subsession:123456:session:0");
        var original = CustomEventId.CreateDeterministic(
            session,
            CreatePosition(sessionNumber: 0, milliseconds: 123_456));

        var changedSession = CustomEventId.CreateDeterministic(
            CreateSession("v1:subsession:654321:session:0"),
            CreatePosition(sessionNumber: 0, milliseconds: 123_456));
        var changedReplaySession = CustomEventId.CreateDeterministic(
            session,
            CreatePosition(sessionNumber: 1, milliseconds: 123_456));
        var changedTime = CustomEventId.CreateDeterministic(
            session,
            CreatePosition(sessionNumber: 0, milliseconds: 123_457));

        Assert.AreNotEqual(original, changedSession);
        Assert.AreNotEqual(original, changedReplaySession);
        Assert.AreNotEqual(original, changedTime);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CustomEventIdRehydratesOnlyCanonicalUuidVersionFiveValues()
    {
        var original = CustomEventId.CreateDeterministic(
            CreateSession("v1:subsession:123456:session:0"),
            CreatePosition(sessionNumber: 0, milliseconds: 123_456));

        Assert.AreEqual(original, CustomEventId.TryCreate(original.Value).Value);
        Assert.AreEqual(original, CustomEventId.TryParse(original.ToString()).Value);

        AssertInvalidEventId(CustomEventId.TryCreate(Guid.Empty));
        AssertInvalidEventId(CustomEventId.TryCreate(Guid.NewGuid()));
        AssertInvalidEventId(CustomEventId.TryCreate(Guid.CreateVersion7()));
        AssertInvalidEventId(CustomEventId.TryParse(null));
        AssertInvalidEventId(CustomEventId.TryParse(string.Empty));
        AssertInvalidEventId(CustomEventId.TryParse(original.ToString().ToUpperInvariant()));
        AssertInvalidEventId(CustomEventId.TryParse($"{{{original}}}"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SubmitterNameNormalizesRequiredSingleLineText()
    {
        var first = SubmitterName.TryCreate("  Jose\u0301 Racer  ").Value;
        var second = SubmitterName.TryCreate("Jos\u00e9 Racer").Value;

        Assert.AreEqual("Jos\u00e9 Racer", first.Value);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreEqual(first.Value, first.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-002")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SubmitterNameEnforcesTextBoundaries()
    {
        var exactLimit = new string('a', SubmitterName.MaximumLength);

        Assert.AreEqual(exactLimit, SubmitterName.TryCreate(exactLimit).Value.Value);
        Assert.IsTrue(SubmitterName.TryCreate("Driver \ud83c\udfc1").IsSuccess);

        AssertInvalidSubmitter(null);
        AssertInvalidSubmitter(string.Empty);
        AssertInvalidSubmitter(" \t ");
        AssertInvalidSubmitter(new string('a', SubmitterName.MaximumLength + 1));
        AssertInvalidSubmitter("Line 1\nLine 2");
        AssertInvalidSubmitter("a\0b");
        AssertInvalidSubmitter("a\ud800");
        AssertInvalidSubmitter("a\udc00b");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void CustomEventValuesCannotBePubliclyConstructedOrMutated()
    {
        Type[] types = [typeof(CustomEventId), typeof(SubmitterName)];

        foreach (var type in types)
        {
            Assert.IsTrue(type.IsSealed, type.FullName);
            Assert.IsEmpty(type.GetConstructors(), type.FullName);
            Assert.IsTrue(
                type.GetProperties().All(static property => property.SetMethod is null),
                type.FullName);
        }
    }

    private static SessionIdentity CreateSession(string sessionKey) =>
        SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate(sessionKey).Value);

    private static ReplayPosition CreatePosition(int sessionNumber, long milliseconds) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionTime.TryCreateMilliseconds(milliseconds).Value).Value;

    private static void AssertInvalidEventId(
        IncidentReview.Results.Result<CustomEventId> result) =>
        DomainTestAssertions.IsValidationFailure(
            result,
            "domain.custom-event-id.invalid");

    private static void AssertInvalidSubmitter(string? value) =>
        DomainTestAssertions.IsValidationFailure(
            SubmitterName.TryCreate(value),
            "domain.submitter-name.invalid");
}
