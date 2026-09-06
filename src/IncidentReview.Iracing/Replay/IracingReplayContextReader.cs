using IncidentReview.Replay.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Iracing.Replay;

internal sealed class IracingReplayContextReader : IReplayContextReader
{
    private readonly IracingConnectionState _connectionState;

    public IracingReplayContextReader(IracingConnectionState connectionState)
    {
        _connectionState = connectionState;
    }

    public Result<ReplayContext> Read()
    {
        var observation = _connectionState.Read();
        if (!observation.IsAvailable)
        {
            return Result<ReplayContext>.Failure(IracingErrors.ReplayUnavailable);
        }

        var frame = observation.Frame;
        var metadata = frame?.Metadata ?? Protocol.IracingReplayContextMetadata.Empty;
        var driverContext = ReplayContext.TryCreate(
            metadata.Player?.DriverDisplayName,
            [],
            currentCameraGroup: null);
        var driverDisplayName = driverContext.IsSuccess
            ? driverContext.Value.DriverDisplayName
            : null;

        var currentCameraGroup = frame?.CameraGroupNumber is { } currentGroupNumber
            ? metadata.CameraGroups.SingleOrDefault(group => group.Number == currentGroupNumber)?.Name
            : null;
        var cameraContext = ReplayContext.TryCreate(
            driverDisplayName: null,
            metadata.CameraGroups.Select(static group => group.Name),
            currentCameraGroup);
        if (!cameraContext.IsSuccess)
        {
            cameraContext = ReplayContext.TryCreate(
                driverDisplayName: null,
                [],
                currentCameraGroup: null);
        }

        return ReplayContext.TryCreate(
            driverDisplayName,
            cameraContext.Value.CameraGroups,
            cameraContext.Value.CurrentCameraGroup);
    }
}
