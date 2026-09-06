using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies a simulator-neutral replay position.</summary>
public sealed record ReplayPosition
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.replay-position.invalid",
        "A replay position requires a session number and session time.");

    private ReplayPosition(SessionNumber sessionNumber, SessionTime sessionTime)
    {
        SessionNumber = sessionNumber;
        SessionTime = sessionTime;
    }

    /// <summary>Gets the session containing the position.</summary>
    public SessionNumber SessionNumber { get; }

    /// <summary>Gets the session-relative time.</summary>
    public SessionTime SessionTime { get; }

    /// <summary>Combines validated values into a replay position.</summary>
    public static Result<ReplayPosition> TryCreate(
        SessionNumber? sessionNumber,
        SessionTime? sessionTime) =>
        sessionNumber is not null && sessionTime is not null
            ? Result<ReplayPosition>.Success(new ReplayPosition(sessionNumber, sessionTime))
            : Result<ReplayPosition>.Failure(InvalidError);
}
