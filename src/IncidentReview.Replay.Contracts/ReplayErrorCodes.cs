using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>
/// Stable error codes owned by the replay contract boundary.
/// </summary>
public static class ReplayErrorCodes
{
    /// <summary>Replay context contains invalid driver or camera-group text.</summary>
    public static ErrorCode InvalidContext { get; } =
        ErrorCode.Define("replay.context.invalid");

    /// <summary>The requested playback rate is not finite and positive.</summary>
    public static ErrorCode InvalidPlaybackRate { get; } =
        ErrorCode.Define("replay.playback-rate.invalid");
}
