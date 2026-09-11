using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Loads one custom event by deterministic identity.</summary>
public sealed record GetCustomEvent : IStoreQuery<StoreLookup<StoredCustomEvent>>
{
    public GetCustomEvent(CustomEventId customEvent)
    {
        ArgumentNullException.ThrowIfNull(customEvent);
        CustomEvent = customEvent;
    }

    public CustomEventId CustomEvent { get; }
}

/// <summary>Loads all custom events for one session in chronological order.</summary>
public sealed record GetCustomEvents : IStoreQuery<IReadOnlyList<StoredCustomEvent>>
{
    public GetCustomEvents(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}

/// <summary>Loads the pending outbox events for one joined session.</summary>
public sealed record GetPendingCustomEvents : IStoreQuery<IReadOnlyList<StoredCustomEvent>>
{
    public GetPendingCustomEvents(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}
