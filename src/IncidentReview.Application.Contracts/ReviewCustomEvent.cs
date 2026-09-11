using IncidentReview.Domain;

namespace IncidentReview.Application.Contracts;

/// <summary>An immutable user-created marker suitable for presentation.</summary>
public sealed record ReviewCustomEvent
{
    private ReviewCustomEvent(
        CustomEventId id,
        SessionIdentity session,
        ReplayPosition position,
        SubmitterName submitter,
        UtcInstant occurredAt,
        bool isSynchronized)
    {
        Id = id;
        Session = session;
        Position = position;
        Submitter = submitter;
        OccurredAt = occurredAt;
        IsSynchronized = isSynchronized;
    }

    public CustomEventId Id { get; }
    public SessionIdentity Session { get; }
    public ReplayPosition Position { get; }
    public SubmitterName Submitter { get; }
    public UtcInstant OccurredAt { get; }
    public bool IsSynchronized { get; }

    public static ReviewCustomEvent Create(
        CustomEventId id,
        SessionIdentity session,
        ReplayPosition position,
        SubmitterName submitter,
        UtcInstant occurredAt,
        bool isSynchronized)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(submitter);
        ArgumentNullException.ThrowIfNull(occurredAt);
        return new ReviewCustomEvent(
            id, session, position, submitter, occurredAt, isSynchronized);
    }
}
