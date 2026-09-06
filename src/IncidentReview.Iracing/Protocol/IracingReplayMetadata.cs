namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingReplayMetadata(
    int PlayerCarIndex,
    int PlayerCarNumberRaw,
    IReadOnlyList<IracingCameraGroupMetadata> CameraGroups);

internal sealed record IracingReplayContextMetadata(
    int? CurrentSessionNumber,
    IracingPlayerMetadata? Player,
    IReadOnlyList<IracingCameraGroupMetadata> CameraGroups,
    IReadOnlyList<IracingReplayParticipantMetadata> Participants)
{
    public static IracingReplayContextMetadata Empty { get; } = new(null, null, [], []);
}

internal sealed record IracingPlayerMetadata(
    int CarIndex,
    int CarNumberRaw,
    string? DriverDisplayName);

internal sealed record IracingReplayParticipantMetadata(
    string Identity,
    int CarIndex,
    int CarNumberRaw,
    int? TeamId,
    int? UserId);

internal sealed record IracingCameraGroupMetadata(
    int Number,
    string Name,
    IReadOnlyList<int> CameraNumbers);
