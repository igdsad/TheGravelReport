namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingIncidentMetadata(
    int? CurrentSessionNumber,
    int? PlayerCarIndex,
    IReadOnlyList<IracingParticipantIncidentMetadata> Participants)
{
    public static IracingIncidentMetadata Empty { get; } = new(null, null, []);
}

internal sealed record IracingParticipantIncidentMetadata(
    int CarIndex,
    string Identity,
    int? IncidentCount,
    int? TeamId,
    int? UserId,
    string? DriverName,
    string? TeamName,
    string? CarNumber,
    int? CarNumberRaw);
