using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>
/// Immutable simulator-neutral data sent when a driver marks a replay moment.
/// </summary>
public sealed record CustomEventSubmission
{
    private CustomEventSubmission(
        CustomEventId id,
        SessionIdentity sessionIdentity,
        ReplayPosition replayPosition,
        SubmitterName submitter,
        UtcInstant occurredAt)
    {
        Id = id;
        SessionIdentity = sessionIdentity;
        ReplayPosition = replayPosition;
        Submitter = submitter;
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the deterministic RFC UUID version 5 identity.</summary>
    public CustomEventId Id { get; }

    /// <summary>Gets the deterministic session containing the event.</summary>
    public SessionIdentity SessionIdentity { get; }

    /// <summary>Gets the exact simulator replay position marked by the driver.</summary>
    public ReplayPosition ReplayPosition { get; }

    /// <summary>Gets the normalized display name supplied by the submitting driver.</summary>
    public SubmitterName Submitter { get; }

    /// <summary>Gets the UTC instant at which the marker was created locally.</summary>
    public UtcInstant OccurredAt { get; }

    /// <summary>Validates event data entering or leaving a synchronization boundary.</summary>
    public static Result<CustomEventSubmission> TryCreate(
        CustomEventId? id,
        SessionIdentity? sessionIdentity,
        ReplayPosition? replayPosition,
        SubmitterName? submitter,
        UtcInstant? occurredAt)
    {
        if (id is null ||
            sessionIdentity is null ||
            replayPosition is null ||
            submitter is null ||
            occurredAt is null ||
            id != CustomEventId.CreateDeterministic(sessionIdentity, replayPosition))
        {
            return Result<CustomEventSubmission>.Failure(EventSyncErrors.InvalidSubmission);
        }

        return Result<CustomEventSubmission>.Success(new CustomEventSubmission(
            id,
            sessionIdentity,
            replayPosition,
            submitter,
            occurredAt));
    }
}
