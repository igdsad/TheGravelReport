using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

public sealed record GetIncident : IStoreQuery<StoreLookup<StoredIncident>>
{
    public GetIncident(IncidentId incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        Incident = incident;
    }

    public IncidentId Incident { get; }
}

public sealed record GetIncidents : IStoreQuery<IReadOnlyList<StoredIncident>>
{
    public GetIncidents(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}

public sealed record GetIncidentCheckpoint : IStoreQuery<StoreLookup<IncidentCheckpoint>>
{
    public GetIncidentCheckpoint(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}
