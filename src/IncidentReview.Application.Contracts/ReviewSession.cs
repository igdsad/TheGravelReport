using IncidentReview.Domain;

namespace IncidentReview.Application.Contracts;

/// <summary>An immutable session and its consistently loaded incidents.</summary>
public sealed record ReviewSession
{
    private ReviewSession(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        IReadOnlyList<ReviewIncident> incidents,
        IReadOnlyList<ReviewCustomEvent> customEvents)
    {
        Id = id;
        Descriptor = descriptor;
        StartedAt = startedAt;
        Incidents = incidents;
        CustomEvents = customEvents;
    }

    public SessionIdentity Id { get; }
    public SimulatorSessionDescriptor Descriptor { get; }
    public UtcInstant StartedAt { get; }
    public IReadOnlyList<ReviewIncident> Incidents { get; }
    public IReadOnlyList<ReviewCustomEvent> CustomEvents { get; }

    public static ReviewSession Create(
        SessionIdentity id,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt,
        IEnumerable<ReviewIncident> incidents,
        IEnumerable<ReviewCustomEvent>? customEvents = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(startedAt);
        ArgumentNullException.ThrowIfNull(incidents);
        customEvents ??= [];
        var snapshot = Array.AsReadOnly(incidents.ToArray());
        if (snapshot.Any(incident => incident is null || incident.Session != id))
        {
            throw new ArgumentException(
                "Incidents must be non-null and belong to the supplied session.",
                nameof(incidents));
        }

        var customEventSnapshot = Array.AsReadOnly(customEvents.ToArray());
        if (customEventSnapshot.Any(customEvent =>
                customEvent is null || customEvent.Session != id))
        {
            throw new ArgumentException(
                "Custom events must be non-null and belong to the supplied session.",
                nameof(customEvents));
        }

        return new ReviewSession(id, descriptor, startedAt, snapshot, customEventSnapshot);
    }
}
