namespace IncidentReview.Store.Contracts;

/// <summary>A durable session and incidents loaded from one consistent snapshot.</summary>
public sealed record StoredSessionDetails
{
    private StoredSessionDetails(StoredSession session, IReadOnlyList<StoredIncident> incidents)
    {
        Session = session;
        Incidents = incidents;
    }

    public StoredSession Session { get; }
    public IReadOnlyList<StoredIncident> Incidents { get; }

    public static StoredSessionDetails Create(
        StoredSession session,
        IEnumerable<StoredIncident> incidents)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(incidents);
        var snapshot = Array.AsReadOnly(incidents.ToArray());
        if (snapshot.Any(incident => incident is null || incident.Session != session.Id))
        {
            throw new ArgumentException(
                "Incidents must be non-null and belong to the supplied session.",
                nameof(incidents));
        }

        return new StoredSessionDetails(session, snapshot);
    }
}
