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
    /// Synchronously hands a validated seek intent to the replay integration boundary.
    /// </summary>
    /// <remarks>
    /// Success means the command was accepted for delivery before this method
    /// returns; it does not assert that the simulator subsequently applied it.
    /// Implementations must not defer delivery or call back into the application.
    /// Cancellation observed before delivery propagates as
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    public Result Seek(
        ReplayPosition position,
        CancellationToken cancellationToken);

    /// <summary>
    /// Synchronously hands a validated pause or playback-rate intent to the replay
    /// integration boundary.
    /// </summary>
    /// <remarks>
    /// Delivery is complete before this method returns. Implementations must not
    /// defer delivery or call back into the application. Cancellation observed
    /// before delivery propagates as <see cref="OperationCanceledException"/>.
    /// </remarks>
    public Result SetPlayback(
        ReplayPlayback playback,
        CancellationToken cancellationToken);
}
