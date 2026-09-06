using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>
/// Controls replay using simulator-neutral, validated intent.
/// </summary>
public interface IReplayController
{
    /// <summary>
    /// Hands a validated seek intent to the replay integration boundary.
    /// </summary>
    /// <remarks>
    /// Success means the command was accepted for delivery; it does not assert
    /// that the simulator subsequently applied it. Cancellation observed before
    /// delivery propagates as <see cref="OperationCanceledException"/>.
    /// </remarks>
    public Task<Result> SeekAsync(
        ReplayPosition position,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands a validated pause or playback-rate intent to the replay integration boundary.
    /// </summary>
    /// <remarks>
    /// Cancellation observed before delivery propagates as
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    public Task<Result> SetPlaybackAsync(
        ReplayPlayback playback,
        CancellationToken cancellationToken);
}
