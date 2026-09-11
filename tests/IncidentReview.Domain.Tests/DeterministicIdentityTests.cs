using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class DeterministicIdentityTests
{
    private const string DurableSessionId = "bf937652-2637-559b-9e6d-91ba750e19a8";
    private const string DetectedIncidentId = "6e7b8f82-bec1-5722-880c-f40bfd7ad54c";

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurableSessionIdentityIsStableAndPinnedToItsCanonicalEvidence()
    {
        var simulator = SimulatorCode.TryCreate("iracing").Value;
        var sessionKey = SimulatorSessionKey.TryCreate(
            "v1:subsession:123456:session:0").Value;

        var first = SessionIdentity.CreateDurable(simulator, sessionKey);
        var second = SessionIdentity.CreateDurable(simulator, sessionKey);

        Assert.AreEqual(first, second);
        Assert.AreEqual(DurableSessionId, first.ToString());
        Assert.AreEqual(5, first.Value.Version);
        Assert.IsTrue(first.Value.Variant is >= 8 and <= 11);
        Assert.AreEqual(first, SessionIdentity.TryCreate(first.Value).Value);
        Assert.AreEqual(first, SessionIdentity.TryParse(first.ToString()).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurableSessionIdentitySeparatesEveryIdentityComponent()
    {
        var iracing = SimulatorCode.TryCreate("iracing").Value;
        var anotherSimulator = SimulatorCode.TryCreate("acc").Value;
        var sessionKey = SimulatorSessionKey.TryCreate("session-key").Value;
        var anotherSessionKey = SimulatorSessionKey.TryCreate("another-session-key").Value;

        var expected = SessionIdentity.CreateDurable(iracing, sessionKey);

        Assert.AreNotEqual(
            expected,
            SessionIdentity.CreateDurable(anotherSimulator, sessionKey));
        Assert.AreNotEqual(
            expected,
            SessionIdentity.CreateDurable(iracing, anotherSessionKey));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurableSessionIdentityCompositionCannotCollideAtComponentBoundaries()
    {
        var first = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("ab").Value,
            SimulatorSessionKey.TryCreate("c").Value);
        var second = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("a").Value,
            SimulatorSessionKey.TryCreate("bc").Value);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DetectedIncidentIdentityIsStableAndPinnedToItsUniquenessKey()
    {
        var session = CreateDurableSession();
        var participant = CreateParticipantIdentity();
        var epoch = CounterEpoch.TryCreate(0).Value;
        var firstPoints = IncidentPoints.TryCreate(total: 4, delta: 4).Value;
        var sameTotalDifferentDelta = IncidentPoints.TryCreate(total: 4, delta: 1).Value;

        var first = IncidentId.CreateDetected(session, participant, epoch, firstPoints);
        var second = IncidentId.CreateDetected(session, participant, epoch, sameTotalDifferentDelta);

        Assert.AreEqual(first, second);
        Assert.AreEqual(DetectedIncidentId, first.ToString());
        Assert.AreEqual(5, first.Value.Version);
        Assert.IsTrue(first.Value.Variant is >= 8 and <= 11);
        Assert.AreEqual(first, IncidentId.TryCreate(first.Value).Value);
        Assert.AreEqual(first, IncidentId.TryParse(first.ToString()).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DetectedIncidentIdentitySeparatesEveryUniquenessComponent()
    {
        var session = CreateDurableSession();
        var participant = CreateParticipantIdentity();
        var anotherParticipant = ParticipantIdentity.TryCreate("car-index:3:team:33").Value;
        var anotherSession = SessionIdentity.CreateDurable(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("v1:subsession:654321:session:0").Value);
        var epoch = CounterEpoch.TryCreate(0).Value;
        var anotherEpoch = CounterEpoch.TryCreate(1).Value;
        var points = IncidentPoints.TryCreate(total: 4, delta: 4).Value;
        var anotherTotal = IncidentPoints.TryCreate(total: 5, delta: 1).Value;

        var expected = IncidentId.CreateDetected(session, participant, epoch, points);

        Assert.AreNotEqual(
            expected,
            IncidentId.CreateDetected(anotherSession, participant, epoch, points));
        Assert.AreNotEqual(
            expected,
            IncidentId.CreateDetected(session, anotherParticipant, epoch, points));
        Assert.AreNotEqual(
            expected,
            IncidentId.CreateDetected(session, participant, anotherEpoch, points));
        Assert.AreNotEqual(
            expected,
            IncidentId.CreateDetected(session, participant, epoch, anotherTotal));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IdentityFactoriesRejectUnsupportedVersionAndVariantValues()
    {
        var nonRfcUuid5 = Guid.ParseExact(
            "bf937652-2637-559b-1e6d-91ba750e19a8",
            "D");
        var uuidVersion4 = Guid.ParseExact(
            "47d740e5-d27a-44ad-a0cd-36482fb346ba",
            "D");

        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryCreate(nonRfcUuid5),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryCreate(nonRfcUuid5),
            "domain.incident-id.invalid");
        DomainTestAssertions.IsValidationFailure(
            SessionIdentity.TryCreate(uuidVersion4),
            "domain.session-identity.invalid");
        DomainTestAssertions.IsValidationFailure(
            IncidentId.TryCreate(uuidVersion4),
            "domain.incident-id.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DeterministicFactoriesRejectMissingValidatedComponents()
    {
        var simulator = SimulatorCode.TryCreate("iracing").Value;
        var sessionKey = SimulatorSessionKey.TryCreate("session-key").Value;
        var session = CreateDurableSession();
        var participant = CreateParticipantIdentity();
        var epoch = CounterEpoch.TryCreate(0).Value;
        var points = IncidentPoints.TryCreate(total: 4, delta: 4).Value;

        Assert.ThrowsExactly<ArgumentNullException>(
            () => SessionIdentity.CreateDurable(null!, sessionKey));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => SessionIdentity.CreateDurable(simulator, null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => IncidentId.CreateDetected(null!, participant, epoch, points));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => IncidentId.CreateDetected(session, null!, epoch, points));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => IncidentId.CreateDetected(session, participant, null!, points));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => IncidentId.CreateDetected(session, participant, epoch, null!));
    }

    private static SessionIdentity CreateDurableSession() => SessionIdentity.CreateDurable(
        SimulatorCode.TryCreate("iracing").Value,
        SimulatorSessionKey.TryCreate("v1:subsession:123456:session:0").Value);

    private static ParticipantIdentity CreateParticipantIdentity() =>
        ParticipantIdentity.TryCreate("car-index:2:team:22").Value;
}
