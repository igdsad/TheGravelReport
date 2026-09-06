using System.Security.AccessControl;
using System.Security.Principal;
using IncidentReview.Iracing.DependencyInjection;
using IncidentReview.Iracing.Options;
using IncidentReview.Iracing.Testing;
using IncidentReview.Replay.Contracts;
using IncidentReview.Telemetry.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentReview.Iracing.Tests;

[TestClass]
public sealed class IracingConfigurationTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    public void AddIracingIntegrationRegistersAllContractsAsSingletonsIdempotently()
    {
        var services = new ServiceCollection();

        var returned = services.AddIracingIntegration();
        _ = services.AddIracingIntegration();

        Assert.AreSame(services, returned);
        var telemetry = services
            .Where(static item => item.ServiceType == typeof(ITelemetrySource))
            .ToArray();
        var replay = services
            .Where(static item => item.ServiceType == typeof(IReplayController))
            .ToArray();
        var replayContext = services
            .Where(static item => item.ServiceType == typeof(IReplayContextReader))
            .ToArray();
        Assert.HasCount(1, telemetry);
        Assert.HasCount(1, replay);
        Assert.HasCount(1, replayContext);
        Assert.AreEqual(ServiceLifetime.Singleton, telemetry[0].Lifetime);
        Assert.AreEqual(ServiceLifetime.Singleton, replay[0].Lifetime);
        Assert.AreEqual(ServiceLifetime.Singleton, replayContext[0].Lifetime);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DataValidEventBoundaryOpensSynchronizeOnlyAclAndReleasesItsHandle()
    {
        var eventName = $"Local\\IncidentReviewRestrictedEvent_{Guid.CreateVersion7():N}";
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
        var security = new EventWaitHandleSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new EventWaitHandleAccessRule(
            user,
            EventWaitHandleRights.Synchronize,
            AccessControlType.Allow));
        using var producerEvent = EventWaitHandleAcl.Create(
            initialState: true,
            EventResetMode.ManualReset,
            eventName,
            out var createdNew,
            security);

        Assert.IsTrue(createdNew);
        Assert.IsTrue(IracingTestingRegistration.CanOpenDataValidEvent(eventName));
        Assert.IsTrue(
            IracingTestingRegistration.CanOpenDataValidEvent(eventName),
            "Releasing the consumer handle must not close the producer-owned named event.");
        Assert.IsTrue(producerEvent.WaitOne(TimeSpan.Zero));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow(0)]
    [DataRow(60_001)]
    public void TryCreateOptionsRejectsIntervalsOutsideConservativeBounds(
        int reconnectMilliseconds)
    {
        var result = IracingOptions.TryCreate(
            TimeSpan.FromMilliseconds(reconnectMilliseconds),
            TimeSpan.FromMilliseconds(100));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.InvalidOptions, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void TryCreateOptionsAcceptsWholeMillisecondBoundaries()
    {
        var minimum = IracingOptions.TryCreate(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
        var maximum = IracingOptions.TryCreate(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        Assert.IsTrue(minimum.IsSuccess);
        Assert.IsTrue(maximum.IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-001")]
    [DataRow("WeekendInfo:\n SubSessionID: 42\n", 42L)]
    [DataRow("WeekendInfo:\n SubSessionID: '42' # stable\n", 42L)]
    [DataRow("Other:\n SubSessionID: 9\nWeekendInfo:\n Track: T\n", null)]
    [DataRow("Other:\n WeekendInfo:\n  SubSessionID: 9\n", null)]
    [DataRow("WeekendInfo:\n Nested:\n  SubSessionID: 9\n", null)]
    [DataRow("WeekendInfo:\n\n  Track: T\n  SubSessionID: 42\n", 42L)]
    [DataRow("WeekendInfo:\n SubSessionID: 0\n", null)]
    [DataRow("WeekendInfo:\n SubSessionID: nope\n", null)]
    [DataRow("WeekendInfo:\n SubSessionID: 1\n SubSessionID: 2\n", null)]
    public void SessionInfoParserUsesOnlyPositiveUniqueWeekendEvidence(
        string input,
        long? expected)
    {
        Assert.AreEqual(expected, IracingTestingRegistration.ReadSubSessionId(input));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4097)]
    public void TryCreateOptionsRejectsEventBufferCapacityOutsideBounds(int capacity)
    {
        var result = IracingOptions.TryCreate(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(25),
            capacity);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.InvalidOptions, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow(2)]
    [DataRow(4096)]
    public void TryCreateOptionsAcceptsEventBufferCapacityBoundaries(int capacity)
    {
        var result = IracingOptions.TryCreate(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(25),
            capacity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(capacity, result.Value.EventBufferCapacity);
    }
}
