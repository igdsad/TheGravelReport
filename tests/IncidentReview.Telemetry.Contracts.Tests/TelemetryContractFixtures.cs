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

    public static IncidentParticipant CreateParticipant(
        string identity = "participant:1",
        string? driverName = "Driver One",
        string? teamName = "Team One",
        string? carNumber = "01") => IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate(identity).Value,
            driverName,
            teamName,
            carNumber).Value;

    public static ParticipantIncidentCounter CreateParticipantCounter(
        string identity = "participant:1",
        int counter = 4,
        int? lap = null,
        double? lapDistance = null) => ParticipantIncidentCounter.TryCreate(
            CreateParticipant(identity),
            IncidentCounter.TryCreate(counter).Value,
            lap.HasValue ? LapNumber.TryCreate(lap.Value).Value : null,
            lapDistance.HasValue ? LapDistance.TryCreate(lapDistance.Value).Value : null).Value;

    public static UtcInstant CreateObservedAt() =>
        UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_000).Value;
}
