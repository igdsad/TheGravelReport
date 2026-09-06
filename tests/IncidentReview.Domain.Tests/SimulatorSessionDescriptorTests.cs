using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class SimulatorSessionDescriptorTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow("i")]
    [DataRow("iracing")]
    [DataRow("simulator-2")]
    [DataRow("abcdefghijklmnopqrstuvwxyz123456")]
    public void SimulatorCodeAcceptsCanonicalBoundedSyntax(string value)
    {
        var code = SimulatorCode.TryCreate(value).Value;

        Assert.AreEqual(value, code.Value);
        Assert.AreEqual(value, code.ToString());
        Assert.AreEqual(code, SimulatorCode.TryCreate(value).Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("1iracing")]
    [DataRow("-iracing")]
    [DataRow("iracing-")]
    [DataRow("i--racing")]
    [DataRow("i_racing")]
    [DataRow("IRacing")]
    [DataRow("{iracing")]
    [DataRow("iracing ")]
    [DataRow("abcdefghijklmnopqrstuvwxyz1234567")]
    public void SimulatorCodeRejectsEveryInvalidSyntaxCategory(string? value) =>
        DomainTestAssertions.IsValidationFailure(
            SimulatorCode.TryCreate(value),
            "domain.simulator-code.invalid");

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SimulatorSessionKeyIsBoundedOpaqueAndPreservedExactly()
    {
        var opaque = " Key:ABC/123 ";
        var maximum = new string('k', SimulatorSessionKey.MaximumLength);
        var withEmoji = "session-\ud83c\udfc1";

        Assert.AreEqual(opaque, SimulatorSessionKey.TryCreate(opaque).Value.Value);
        Assert.AreEqual(maximum, SimulatorSessionKey.TryCreate(maximum).Value.Value);
        Assert.AreEqual(withEmoji, SimulatorSessionKey.TryCreate(withEmoji).Value.Value);
        Assert.AreEqual(opaque, SimulatorSessionKey.TryCreate(opaque).Value.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SimulatorSessionKeyRejectsInvalidBoundaryText()
    {
        AssertInvalidSessionKey(null);
        AssertInvalidSessionKey(string.Empty);
        AssertInvalidSessionKey(" \t ");
        AssertInvalidSessionKey(new string('k', SimulatorSessionKey.MaximumLength + 1));
        AssertInvalidSessionKey("key\0value");
        AssertInvalidSessionKey("key\nvalue");
        AssertInvalidSessionKey("key\ud800");
        AssertInvalidSessionKey("key\ud800X");
        AssertInvalidSessionKey("key\udc00");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void SessionModesAndIdentityScopesHaveStablePersistedCodes()
    {
        Assert.AreSame(SessionMode.Live, SessionMode.TryCreate(1).Value);
        Assert.AreEqual(1, SessionMode.Live.Value);
        Assert.AreEqual("Live", SessionMode.Live.ToString());
        Assert.AreSame(SessionMode.Replay, SessionMode.TryCreate(2).Value);
        Assert.AreEqual(2, SessionMode.Replay.Value);
        Assert.AreEqual("Replay", SessionMode.Replay.ToString());

        Assert.AreSame(SimulatorIdentityScope.Durable, SimulatorIdentityScope.TryCreate(1).Value);
        Assert.AreEqual(1, SimulatorIdentityScope.Durable.Value);
        Assert.AreEqual("Durable", SimulatorIdentityScope.Durable.ToString());
        Assert.AreSame(
            SimulatorIdentityScope.ConnectionScoped,
            SimulatorIdentityScope.TryCreate(2).Value);
        Assert.AreEqual(2, SimulatorIdentityScope.ConnectionScoped.Value);
        Assert.AreEqual("ConnectionScoped", SimulatorIdentityScope.ConnectionScoped.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(int.MinValue)]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(int.MaxValue)]
    public void SessionModeAndIdentityScopeRejectUnknownCodes(int value)
    {
        DomainTestAssertions.IsValidationFailure(
            SessionMode.TryCreate(value),
            "domain.session-mode.invalid");
        DomainTestAssertions.IsValidationFailure(
            SimulatorIdentityScope.TryCreate(value),
            "domain.simulator-identity-scope.invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DescriptorCombinesValidatedValuesWithStructuralEquality()
    {
        var simulator = SimulatorCode.TryCreate("iracing").Value;
        var key = SimulatorSessionKey.TryCreate("subsession:42").Value;
        var number = SessionNumber.TryCreate(3).Value;

        var first = SimulatorSessionDescriptor.TryCreate(
            simulator,
            key,
            number,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;
        var second = SimulatorSessionDescriptor.TryCreate(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("subsession:42").Value,
            SessionNumber.TryCreate(3).Value,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;

        Assert.AreSame(simulator, first.Simulator);
        Assert.AreSame(key, first.SessionKey);
        Assert.AreSame(number, first.SessionNumber);
        Assert.AreSame(SessionMode.Live, first.Mode);
        Assert.AreSame(SimulatorIdentityScope.Durable, first.IdentityScope);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DescriptorIndependentlyRejectsEveryMissingComponent()
    {
        var simulator = SimulatorCode.TryCreate("iracing").Value;
        var key = SimulatorSessionKey.TryCreate("session:42").Value;
        var number = SessionNumber.TryCreate(0).Value;
        var mode = SessionMode.Live;
        var scope = SimulatorIdentityScope.Durable;

        AssertInvalidDescriptor(null, key, number, mode, scope);
        AssertInvalidDescriptor(simulator, null, number, mode, scope);
        AssertInvalidDescriptor(simulator, key, null, mode, scope);
        AssertInvalidDescriptor(simulator, key, number, null, scope);
        AssertInvalidDescriptor(simulator, key, number, mode, null);
        AssertInvalidDescriptor(null, null, null, null, null);
    }

    private static void AssertInvalidSessionKey(string? key) =>
        DomainTestAssertions.IsValidationFailure(
            SimulatorSessionKey.TryCreate(key),
            "domain.simulator-session-key.invalid");

    private static void AssertInvalidDescriptor(
        SimulatorCode? simulator,
        SimulatorSessionKey? key,
        SessionNumber? number,
        SessionMode? mode,
        SimulatorIdentityScope? scope) => DomainTestAssertions.IsValidationFailure(
            SimulatorSessionDescriptor.TryCreate(simulator, key, number, mode, scope),
            "domain.simulator-session-descriptor.invalid");
}
