using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>
/// Controls replay using simulator-neutral, validated intent.
/// </summary>
public interface IReplayController
{
    /// <summary>
    /// Validates that a simulator-neutral playback intent is exactly representable by this controller.
    /// </summary>
    /// <remarks>This pure preflight does not deliver a replay command.</remarks>
    public Result ValidatePlayback(ReplayPlayback playback);

    /// <summary>
    /// Delivers a seek intent and waits until the simulator reports that it was applied.
    /// </summary>
    /// <remarks>
    /// Success means a later simulator frame confirmed the requested replay
    /// session and time. Cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </remarks>
    public ValueTask<Result> SeekAsync(
        ReplayPosition position,
        CancellationToken cancellationToken);

    /// <summary>
    /// Focuses the replay camera on the local player and waits for confirmation.
    /// </summary>
    /// <remarks>
    /// A null preference preserves the currently reported camera. A non-null
    /// preference names an iRacing camera group and selects its first camera.
    /// </remarks>
    public ValueTask<Result> FocusPlayerAsync(
        string? preferredCamera,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delivers a pause or playback-rate intent and waits until the simulator
    /// reports that it was applied.
    /// </summary>
    public ValueTask<Result> SetPlaybackAsync(
        ReplayPlayback playback,
        CancellationToken cancellationToken);
}
