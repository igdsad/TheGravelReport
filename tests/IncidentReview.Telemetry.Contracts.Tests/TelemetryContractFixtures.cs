namespace IncidentReview.Telemetry.Contracts.Tests;

internal static class TelemetryContractFixtures
{
    public static SimulatorSessionDescriptor CreateSession(int sessionNumber = 1) =>
        SimulatorSessionDescriptor.TryCreate(
            SimulatorCode.TryCreate("iracing").Value,
            SimulatorSessionKey.TryCreate("official-evidence:test-session").Value,
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionMode.Live,
            SimulatorIdentityScope.Durable).Value;

    public static ReplayPosition CreatePosition(int sessionNumber = 1) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionTime.TryCreateMilliseconds(12_345).Value).Value;

    public static IncidentCounter CreateCounter() => IncidentCounter.TryCreate(4).Value;

    public static UtcInstant CreateObservedAt() =>
        UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_000).Value;
}
