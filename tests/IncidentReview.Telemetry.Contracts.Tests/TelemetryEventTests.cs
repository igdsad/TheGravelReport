namespace IncidentReview.Telemetry.Contracts.Tests;

[TestClass]
public sealed class TelemetryEventTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void EventHierarchyIsClosedToTheFourDocumentedStates()
    {
        Assert.IsTrue(typeof(TelemetryEvent).IsAbstract);

        var eventTypes = typeof(TelemetryEvent).Assembly.GetExportedTypes()
            .Where(type => type != typeof(TelemetryEvent) &&
                typeof(TelemetryEvent).IsAssignableFrom(type))
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(TelemetryConnected),
                typeof(TelemetryDisconnected),
                typeof(TelemetrySampleObserved),
                typeof(TelemetryUnavailable),
            },
            eventTypes);
        Assert.IsTrue(eventTypes.All(type => type.IsSealed));
        Assert.IsTrue(eventTypes.All(type => type.GetConstructors().Length == 0));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    public void ConnectionStateEventsAreStableImmutableValues()
    {
        var connected = TelemetryConnected.Instance;
        var disconnected = TelemetryDisconnected.Instance;

        Assert.IsInstanceOfType<TelemetryEvent>(connected);
        Assert.IsInstanceOfType<TelemetryEvent>(disconnected);
        Assert.IsNull(typeof(TelemetryConnected)
            .GetProperty(nameof(TelemetryConnected.Instance))!
            .SetMethod);
        Assert.IsNull(typeof(TelemetryDisconnected)
            .GetProperty(nameof(TelemetryDisconnected.Instance))!
            .SetMethod);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    public void SampleEventCarriesExactlyTheValidatedSample()
    {
        var sample = TelemetrySample.TryCreate(
            TelemetryContractFixtures.CreateSession(),
            TelemetryContractFixtures.CreatePosition(),
            TelemetryContractFixtures.CreateCounter(),
            lap: null,
            lapDistance: null,
            OnTrackState.OnTrack,
            TelemetryContractFixtures.CreateObservedAt()).Value;

        var observed = TelemetrySampleObserved.Create(sample);

        Assert.AreSame(sample, observed.Sample);
        Assert.IsInstanceOfType<TelemetryEvent>(observed);
        Assert.IsNull(typeof(TelemetrySampleObserved)
            .GetProperty(nameof(TelemetrySampleObserved.Sample))!
            .SetMethod);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void SampleEventRejectsNullAsAProgrammerDefect()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => TelemetrySampleObserved.Create(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void UnavailableEventCarriesTheStructuredFailure()
    {
        var error = Error.Create(
            ErrorCode.Define("telemetry.source.unavailable"),
            ErrorKind.Unavailable,
            "Telemetry is temporarily unavailable.");

        var unavailable = TelemetryUnavailable.Create(error);

        Assert.AreSame(error, unavailable.Error);
        Assert.IsInstanceOfType<TelemetryEvent>(unavailable);
        Assert.IsNull(typeof(TelemetryUnavailable)
            .GetProperty(nameof(TelemetryUnavailable.Error))!
            .SetMethod);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void UnavailableEventRejectsNullAsAProgrammerDefect()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => TelemetryUnavailable.Create(null!));
    }
}
