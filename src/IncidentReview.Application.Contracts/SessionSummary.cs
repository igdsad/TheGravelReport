using IncidentReview.Domain;

namespace IncidentReview.Application.Contracts;

/// <summary>An immutable session-list row for presentation.</summary>
public sealed record SessionSummary
{
    private SessionSummary(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        int incidentCount,
        int pendingIncidentCount)
    {
        Id = id;
        Descriptor = descriptor;
        StartedAt = startedAt;
        IncidentCount = incidentCount;
        PendingIncidentCount = pendingIncidentCount;
    }

    public SessionIdentity Id { get; }
    public SimulatorSessionDescriptor Descriptor { get; }
    public UtcInstant StartedAt { get; }
    public int IncidentCount { get; }
    public int PendingIncidentCount { get; }

    public static SessionSummary Create(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        int incidentCount,
        int pendingIncidentCount)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(startedAt);
        if (incidentCount < 0 || pendingIncidentCount < 0 || pendingIncidentCount > incidentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(incidentCount));
        }

        return new SessionSummary(id, descriptor, startedAt, incidentCount, pendingIncidentCount);
    }
}
