using IncidentReview.Domain;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Iracing.Replay;

internal static class IracingReplayEncoder
{
    public static Result<ReplayBroadcastCommand> EncodeSeek(ReplayPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.SessionNumber.Value > short.MaxValue ||
            position.SessionTime.Milliseconds > int.MaxValue)
        {
            return Result<ReplayBroadcastCommand>.Failure(
                IracingErrors.UnsupportedReplayPosition);
        }

        return Result<ReplayBroadcastCommand>.Success(new ReplayBroadcastCommand(
            IracingBroadcastMessage.ReplaySearchSessionTime,
            PackInt16Pair(
                (short)IracingBroadcastMessage.ReplaySearchSessionTime,
                (short)position.SessionNumber.Value),
            (int)position.SessionTime.Milliseconds));
    }

    public static Result<ReplayBroadcastCommand> EncodePlayback(ReplayPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        if (playback.IsPaused)
        {
            return Result<ReplayBroadcastCommand>.Success(CreatePlaybackCommand(
                speed: 0,
                slowMotion: false));
        }

        if (playback.PlaybackRate is not { } rate)
        {
            return Result<ReplayBroadcastCommand>.Failure(
                IracingErrors.UnsupportedPlaybackRate);
        }

        if (rate >= 1d &&
            rate <= short.MaxValue &&
            rate == Math.Truncate(rate))
        {
            return Result<ReplayBroadcastCommand>.Success(CreatePlaybackCommand(
                checked((short)rate),
                slowMotion: false));
        }

        var reciprocal = 1d / rate;
        if (rate < 1d &&
            reciprocal <= short.MaxValue &&
            reciprocal == Math.Truncate(reciprocal) &&
            1d / reciprocal == rate)
        {
            return Result<ReplayBroadcastCommand>.Success(CreatePlaybackCommand(
                checked((short)reciprocal),
                slowMotion: true));
        }

        return Result<ReplayBroadcastCommand>.Failure(
            IracingErrors.UnsupportedPlaybackRate);
    }

    private static ReplayBroadcastCommand CreatePlaybackCommand(
        short speed,
        bool slowMotion) => new(
        IracingBroadcastMessage.ReplaySetPlaySpeed,
        PackInt16Pair((short)IracingBroadcastMessage.ReplaySetPlaySpeed, speed),
        PackInt16Pair(slowMotion ? (short)1 : (short)0, 0));

    private static int PackInt16Pair(short low, short high) =>
        unchecked((int)((uint)(ushort)low | ((uint)(ushort)high << 16)));
}
