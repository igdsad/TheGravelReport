namespace IncidentReview.Store.Contracts;

/// <summary>A durable session and incidents loaded from one consistent snapshot.</summary>
public sealed record StoredSessionDetails
{
    private StoredSessionDetails(
        StoredSession session,
        IReadOnlyList<StoredIncident> incidents,
        IReadOnlyList<StoredCustomEvent> customEvents)
    {
        Session = session;
        Incidents = incidents;
        CustomEvents = customEvents;
    }

    public StoredSession Session { get; }
    public IReadOnlyList<StoredIncident> Incidents { get; }
    public IReadOnlyList<StoredCustomEvent> CustomEvents { get; }

    public static StoredSessionDetails Create(
        StoredSession session,
        IEnumerable<StoredIncident> incidents,
        IEnumerable<StoredCustomEvent>? customEvents = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(incidents);
        customEvents ??= [];
        var snapshot = Array.AsReadOnly(incidents.ToArray());
        if (snapshot.Any(incident => incident is null || incident.Session != session.Id))
        {
            throw new ArgumentException(
                "Incidents must be non-null and belong to the supplied session.",
                nameof(incidents));
        }

        var customEventSnapshot = Array.AsReadOnly(customEvents.ToArray());
        if (customEventSnapshot.Any(customEvent =>
                customEvent is null || customEvent.Session != session.Id))
        {
            throw new ArgumentException(
                "Custom events must be non-null and belong to the supplied session.",
                nameof(customEvents));
        }

        return new StoredSessionDetails(session, snapshot, customEventSnapshot);
    }
}
