using IncidentReview.Domain;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Iracing.Replay;

internal sealed class IracingReplayController : IReplayController
{
    private static readonly TimeSpan DefaultConfirmationTimeout = TimeSpan.FromSeconds(10);
    private const long SeekToleranceMilliseconds = 250;

    private readonly IReplayMessageSender _sender;
    private readonly IracingConnectionState _connectionState;
    private readonly TimeSpan _confirmationTimeout;

    public IracingReplayController(
        IReplayMessageSender sender,
        IracingConnectionState connectionState,
        TimeSpan? confirmationTimeout = null)
    {
        _sender = sender;
        _connectionState = connectionState;
        _confirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
    }

    public Result ValidatePlayback(ReplayPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        var command = IracingReplayEncoder.EncodePlayback(playback);
        return command.IsSuccess
            ? Result.Success()
            : Result.Failure(command.Error!);
    }

    public async ValueTask<Result> SeekAsync(
        ReplayPosition position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        cancellationToken.ThrowIfCancellationRequested();
        var command = IracingReplayEncoder.EncodeSeek(position);
        if (!command.IsSuccess)
        {
            return Result.Failure(command.Error!);
        }

        var baseline = _connectionState.Read().Version;
        var delivery = Deliver(command.Value, cancellationToken);
        if (!delivery.IsSuccess)
        {
            return delivery;
        }

        return await ConfirmAsync(
            baseline,
            frame =>
                frame.ReplaySessionNumber == position.SessionNumber.Value &&
                frame.ReplaySessionTimeMilliseconds is { } actualMilliseconds &&
                IsWithinSeekTolerance(actualMilliseconds, position.SessionTime.Milliseconds),
            IracingErrors.ReplaySeekTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Result> FocusParticipantAsync(
        IncidentParticipant participant,
        string? preferredCamera,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();
        var observation = _connectionState.Read();
        if (observation is not ReplayFrameObservation.Available available)
        {
            return Result.Failure(IracingErrors.ReplayUnavailable);
        }

        var metadata = available.Frame.Metadata;
        if (metadata.CameraGroups.Count == 0 ||
            metadata.CurrentSessionNumber is not { } currentSessionNumber ||
            available.Frame.LiveSessionNumber != currentSessionNumber ||
            available.Frame.ReplaySessionNumber != currentSessionNumber)
        {
            return Result.Failure(IracingErrors.ReplayMetadataUnavailable);
        }

        var target = ResolveFocusTarget(metadata, participant.Identity.Value);
        if (target is null)
        {
            return Result.Failure(IracingErrors.ReplayMetadataUnavailable);
        }

        int cameraGroupNumber;
        int cameraNumber;
        if (preferredCamera is null)
        {
            if (available.Frame.CameraGroupNumber is not { } currentGroup ||
                available.Frame.CameraNumber is not { } currentCamera)
            {
                return Result.Failure(IracingErrors.ReplayMetadataUnavailable);
            }

            cameraGroupNumber = currentGroup;
            cameraNumber = currentCamera;
        }
        else
        {
            var cameraGroup = metadata.CameraGroups.SingleOrDefault(group =>
                string.Equals(group.Name, preferredCamera, StringComparison.OrdinalIgnoreCase));
            if (cameraGroup is null)
            {
                return Result.Failure(IracingErrors.ReplayCameraNotFound);
            }

            cameraGroupNumber = cameraGroup.Number;
            cameraNumber = cameraGroup.CameraNumbers[0];
        }

        var command = IracingReplayEncoder.EncodeFocusPlayer(
            target.CarNumberRaw,
            cameraGroupNumber,
            cameraNumber);
        if (!command.IsSuccess)
        {
            return Result.Failure(command.Error!);
        }

        var delivery = Deliver(command.Value, cancellationToken);
        if (!delivery.IsSuccess)
        {
            return delivery;
        }

        return await ConfirmAsync(
            observation.Version,
            frame => IsParticipantFocusApplied(
                frame,
                participant.Identity.Value,
                target,
                currentSessionNumber,
                cameraGroupNumber),
            IracingErrors.ReplayCameraTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsParticipantFocusApplied(
        ReplayFrameState frame,
        string identity,
        IracingReplayParticipantMetadata expectedTarget,
        int expectedSessionNumber,
        int expectedCameraGroupNumber)
    {
        if (frame.Metadata.CurrentSessionNumber != expectedSessionNumber ||
            frame.LiveSessionNumber != expectedSessionNumber ||
            frame.ReplaySessionNumber != expectedSessionNumber ||
            frame.CameraCarIndex != expectedTarget.CarIndex ||
            frame.CameraGroupNumber != expectedCameraGroupNumber)
        {
            return false;
        }

        var currentTarget = ResolveFocusTarget(frame.Metadata, identity);
        return currentTarget is not null &&
            currentTarget.CarIndex == expectedTarget.CarIndex &&
            currentTarget.CarNumberRaw == expectedTarget.CarNumberRaw;
    }

    private static IracingReplayParticipantMetadata? ResolveFocusTarget(
        IracingReplayContextMetadata metadata,
        string identity)
    {
        if (string.Equals(identity, "local-player", StringComparison.Ordinal))
        {
            return metadata.Player is { } player
                ? new IracingReplayParticipantMetadata(
                    identity,
                    player.CarIndex,
                    player.CarNumberRaw,
                    TeamId: null,
                    UserId: null)
                : null;
        }

        return metadata.Participants.SingleOrDefault(participant =>
            string.Equals(participant.Identity, identity, StringComparison.Ordinal));
    }

    public async ValueTask<Result> SetPlaybackAsync(
        ReplayPlayback playback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playback);
        cancellationToken.ThrowIfCancellationRequested();
        var command = IracingReplayEncoder.EncodePlayback(playback);
        if (!command.IsSuccess)
        {
            return Result.Failure(command.Error!);
        }

        var baseline = _connectionState.Read().Version;
        var delivery = Deliver(command.Value, cancellationToken);
        if (!delivery.IsSuccess)
        {
            return delivery;
        }

        var expectedSpeed = unchecked((short)((uint)command.Value.WParam >> 16));
        var expectedSlowMotion = unchecked((short)(uint)command.Value.LParam) != 0;
        return await ConfirmAsync(
            baseline,
            frame =>
                frame.ReplayPlaySpeed == expectedSpeed &&
                frame.ReplayPlaySlowMotion == expectedSlowMotion,
            IracingErrors.ReplayPlaybackTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Result> ConfirmAsync(
        long baselineVersion,
        Func<ReplayFrameState, bool> isApplied,
        Error timeoutError,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(_confirmationTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var observedVersion = baselineVersion;
        try
        {
            while (true)
            {
                var observation = _connectionState.Read();
                if (observation is not ReplayFrameObservation.Available available)
                {
                    return Result.Failure(IracingErrors.ReplayUnavailable);
                }

                if (observation.Version > baselineVersion &&
                    isApplied(available.Frame))
                {
                    return Result.Success();
                }

                observedVersion = observation.Version;
                await _connectionState.WaitForChangeAsync(
                    observedVersion,
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(timeoutError);
        }
    }

    private static bool IsWithinSeekTolerance(long actual, long requested) =>
        actual >= Math.Max(0, requested - SeekToleranceMilliseconds) &&
        actual <= requested + SeekToleranceMilliseconds;

    private Result Deliver(
        ReplayBroadcastCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connectionState.Read() is not ReplayFrameObservation.Available)
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
