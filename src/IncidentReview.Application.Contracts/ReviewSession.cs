using IncidentReview.Domain;

namespace IncidentReview.Application.Contracts;

/// <summary>An immutable session and its consistently loaded incidents.</summary>
public sealed record ReviewSession
{
    private ReviewSession(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        IReadOnlyList<ReviewIncident> incidents)
    {
        Id = id;
        Descriptor = descriptor;
        StartedAt = startedAt;
        Incidents = incidents;
    }

    public SessionIdentity Id { get; }
    public SimulatorSessionDescriptor Descriptor { get; }
    public UtcInstant StartedAt { get; }
    public IReadOnlyList<ReviewIncident> Incidents { get; }

    public static ReviewSession Create(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        IEnumerable<ReviewIncident> incidents)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(startedAt);
        ArgumentNullException.ThrowIfNull(incidents);
        var snapshot = Array.AsReadOnly(incidents.ToArray());
        if (snapshot.Any(static incident => incident is null))
        {
            throw new ArgumentException("Incidents cannot contain null values.", nameof(incidents));
        }

        return new ReviewSession(id, descriptor, startedAt, snapshot);
    }
}
