using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>A complete simulator-neutral durable custom review event.</summary>
public sealed record StoredCustomEvent
{
    private StoredCustomEvent(
        CustomEventId id,
        SessionIdentity session,
        ReplayPosition position,
        SubmitterName submitter,
        UtcInstant occurredAt,
        CustomEventSynchronization synchronization)
    {
        Id = id;
        Session = session;
        Position = position;
        Submitter = submitter;
        OccurredAt = occurredAt;
        Synchronization = synchronization;
    }

    public CustomEventId Id { get; }
    public SessionIdentity Session { get; }
    public ReplayPosition Position { get; }
    public SubmitterName Submitter { get; }
    public UtcInstant OccurredAt { get; }
    public CustomEventSynchronization Synchronization { get; }

    /// <summary>Creates a new local event in the pending outbox state.</summary>
    public static StoredCustomEvent CreatePending(
        SessionIdentity session,
        ReplayPosition position,
        SubmitterName submitter,
        UtcInstant occurredAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(submitter);
        ArgumentNullException.ThrowIfNull(occurredAt);

        return new StoredCustomEvent(
            CustomEventId.CreateDeterministic(session, position),
            session,
            position,
            submitter,
            occurredAt,
            CustomEventSynchronization.Pending.Instance);
    }

    /// <summary>Rehydrates a complete event after all persisted values have been validated.</summary>
    public static StoredCustomEvent Create(
        CustomEventId id,
        SessionIdentity session,
        ReplayPosition position,
        SubmitterName submitter,
        UtcInstant occurredAt,
        CustomEventSynchronization synchronization)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(submitter);
        ArgumentNullException.ThrowIfNull(occurredAt);
        ArgumentNullException.ThrowIfNull(synchronization);

        if (id != CustomEventId.CreateDeterministic(session, position))
        {
            throw new ArgumentException(
                "The custom-event identifier does not match its session and replay position.",
                nameof(id));
        }

        if (synchronization is CustomEventSynchronization.Synchronized synchronized &&
            synchronized.SynchronizedAt.UnixMilliseconds < occurredAt.UnixMilliseconds)
        {
            throw new ArgumentException(
                "The synchronization time cannot precede the event occurrence.",
                nameof(synchronization));
        }

        return new StoredCustomEvent(
            id,
            session,
            position,
            submitter,
            occurredAt,
            synchronization);
    }
}
