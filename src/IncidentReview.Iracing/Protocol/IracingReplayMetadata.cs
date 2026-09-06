namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingReplayMetadata(
    int PlayerCarIndex,
    int PlayerCarNumberRaw,
    IReadOnlyList<IracingCameraGroupMetadata> CameraGroups);

internal sealed record IracingCameraGroupMetadata(
    int Number,
    string Name,
    IReadOnlyList<int> CameraNumbers);
