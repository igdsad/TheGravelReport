using IncidentReview.Domain;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Iracing.Replay;

internal sealed class IracingReplayController : IReplayController
{
    private readonly IReplayMessageSender _sender;
    private readonly IracingConnectionState _connectionState;

    public IracingReplayController(
        IReplayMessageSender sender,
        IracingConnectionState connectionState)
    {
        _sender = sender;
        _connectionState = connectionState;
    }

    public Result ValidatePlayback(ReplayPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        var command = IracingReplayEncoder.EncodePlayback(playback);
        return command.IsSuccess
            ? Result.Success()
            : Result.Failure(command.Error!);
    }

    public Result Seek(
        ReplayPosition position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        cancellationToken.ThrowIfCancellationRequested();
        var command = IracingReplayEncoder.EncodeSeek(position);
        return command.IsSuccess
            ? Deliver(command.Value, cancellationToken)
            : Result.Failure(command.Error!);
    }

    public Result SetPlayback(
        ReplayPlayback playback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playback);
        cancellationToken.ThrowIfCancellationRequested();
        var command = IracingReplayEncoder.EncodePlayback(playback);
        return command.IsSuccess
            ? Deliver(command.Value, cancellationToken)
            : Result.Failure(command.Error!);
    }

    private Result Deliver(
        ReplayBroadcastCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connectionState.IsAvailable)
        {
            return Result.Failure(IracingErrors.ReplayUnavailable);
        }

        return _sender.Send(command) switch
        {
            ReplaySendOutcome.Delivered => Result.Success(),
            ReplaySendOutcome.EndpointUnavailable => Result.Failure(
                IracingErrors.ReplayUnavailable),
            ReplaySendOutcome.DeliveryRejected => Result.Failure(
                IracingErrors.ReplayDeliveryFailed),
            _ => throw new InvalidOperationException("The replay delivery outcome is undefined."),
        };
    }
}
