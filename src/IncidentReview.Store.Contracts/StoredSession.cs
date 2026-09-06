using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>A simulator-neutral durable session record.</summary>
public sealed record StoredSession
{
    private StoredSession(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt)
    {
        Id = id;
        Descriptor = descriptor;
        StartedAt = startedAt;
    }

    public SessionIdentity Id { get; }
    public SimulatorSessionDescriptor Descriptor { get; }
    public UtcInstant StartedAt { get; }

    public static StoredSession Create(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(startedAt);
        return new StoredSession(id, descriptor, startedAt);
    }
}
