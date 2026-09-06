namespace IncidentReview.Store.Contracts;

/// <summary>A durable session plus aggregate incident counts.</summary>
public sealed record StoredSessionSummary
{
    private StoredSessionSummary(StoredSession session, int incidentCount, int pendingIncidentCount)
    {
        Session = session;
        IncidentCount = incidentCount;
        PendingIncidentCount = pendingIncidentCount;
    }

    public StoredSession Session { get; }
    public int IncidentCount { get; }
    public int PendingIncidentCount { get; }

    public static StoredSessionSummary Create(
        StoredSession session,
        int incidentCount,
        int pendingIncidentCount)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (incidentCount < 0 || pendingIncidentCount < 0 || pendingIncidentCount > incidentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(incidentCount));
        }

        return new StoredSessionSummary(session, incidentCount, pendingIncidentCount);
    }
}
