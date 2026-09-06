namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingReplayMetadata(
    int PlayerCarIndex,
    int PlayerCarNumberRaw,
    IReadOnlyList<IracingCameraGroupMetadata> CameraGroups);

internal sealed record IracingReplayContextMetadata(
    IracingPlayerMetadata? Player,
    IReadOnlyList<IracingCameraGroupMetadata> CameraGroups)
{
    public static IracingReplayContextMetadata Empty { get; } = new(null, []);
}

internal sealed record IracingPlayerMetadata(
    int CarIndex,
    int CarNumberRaw,
    string? DriverDisplayName);

internal sealed record IracingCameraGroupMetadata(
    int Number,
    string Name,
    IReadOnlyList<int> CameraNumbers);
