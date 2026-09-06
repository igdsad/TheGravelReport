using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>
/// Represents a validated replay pause or positive playback-rate intent.
/// </summary>
public sealed record ReplayPlayback
{
    private static readonly Error InvalidPlaybackRateError = Error.Create(
        ReplayErrorCodes.InvalidPlaybackRate,
        ErrorKind.Validation,
        "A replay playback rate must be finite and greater than zero.");

    private ReplayPlayback(bool isPaused, double? playbackRate)
    {
        IsPaused = isPaused;
        PlaybackRate = playbackRate;
    }

    /// <summary>
    /// Gets the intent to pause replay without carrying a meaningless rate.
    /// </summary>
    public static ReplayPlayback Paused { get; } = new(
        isPaused: true,
        playbackRate: null);

    /// <summary>
    /// Gets whether replay should be paused.
    /// </summary>
    public bool IsPaused { get; }

    /// <summary>
    /// Gets the positive playback multiplier, or <see langword="null"/> when paused.
    /// </summary>
    public double? PlaybackRate { get; }

    /// <summary>
    /// Creates an intent to play replay at a positive finite multiplier.
    /// </summary>
    public static Result<ReplayPlayback> TryCreatePlaying(double playbackRate) =>
        double.IsFinite(playbackRate) && playbackRate > 0
            ? Result<ReplayPlayback>.Success(new ReplayPlayback(
                isPaused: false,
                playbackRate))
            : Result<ReplayPlayback>.Failure(InvalidPlaybackRateError);
}
