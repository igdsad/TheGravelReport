using System.Reflection;

namespace IncidentReview.Telemetry.Contracts.Tests;

[TestClass]
public sealed class TelemetryApiTests
{
    private static readonly int[] ExpectedOnTrackValues = [0, 1, 2];

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public void SourceExposesOneOrderedCancelableEventStream()
    {
        var methods = typeof(ITelemetrySource).GetMethods();
        Assert.HasCount(1, methods);

        var observe = methods[0];
        Assert.AreEqual(nameof(ITelemetrySource.ObserveAsync), observe.Name);
        Assert.AreEqual(typeof(IAsyncEnumerable<TelemetryEvent>), observe.ReturnType);

        var parameters = observe.GetParameters();
        Assert.HasCount(1, parameters);
        Assert.AreEqual(typeof(CancellationToken), parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
        Assert.IsFalse(parameters[0].HasDefaultValue);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void OnTrackStatesHaveExplicitStableValues()
    {
        var values = Enum.GetValues<OnTrackState>();

        CollectionAssert.AreEquivalent(
            new[]
            {
                OnTrackState.Unknown,
                OnTrackState.NotOnTrack,
                OnTrackState.OnTrack,
            },
            values);
        CollectionAssert.AreEqual(
            ExpectedOnTrackValues,
            values.Select(value => (int)value).ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void AssemblyExportsOnlyTheDeliberateContractSurface()
    {
        var exportedTypes = typeof(ITelemetrySource).Assembly.GetExportedTypes();

        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(ITelemetrySource),
                typeof(OnTrackState),
                typeof(ParticipantIncidentCounter),
                typeof(TelemetryConnected),
                typeof(TelemetryDisconnected),
                typeof(TelemetryErrorCodes),
                typeof(TelemetryEvent),
                typeof(TelemetrySample),
                typeof(TelemetrySampleObserved),
                typeof(TelemetryUnavailable),
            },
            exportedTypes);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void EventBaseCannotBeExtendedOutsideItsOwningAssembly()
    {
        var constructors = typeof(TelemetryEvent).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.HasCount(1, constructors);
        Assert.IsTrue(constructors[0].IsFamilyAndAssembly);
    }
}
