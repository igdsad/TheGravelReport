using IncidentReview.Domain;

namespace IncidentReview.Application.Contracts;

/// <summary>An immutable incident model suitable for presentation.</summary>
public sealed record ReviewIncident
{
    private ReviewIncident(
        IncidentId id,
        SessionIdentity session,
        ReplayPosition position,
        UtcInstant observedAt,
        IncidentPoints points,
        CounterEpoch counterEpoch,
        LapNumber? lap,
        LapDistance? lapDistance,
        IncidentReviewStatus reviewStatus,
        IncidentAnnotation annotation)
    {
        Id = id;
        Session = session;
        Position = position;
        ObservedAt = observedAt;
        Points = points;
        CounterEpoch = counterEpoch;
        Lap = lap;
        LapDistance = lapDistance;
        ReviewStatus = reviewStatus;
        Annotation = annotation;
    }

    public IncidentId Id { get; }
    public SessionIdentity Session { get; }
    public ReplayPosition Position { get; }
    public UtcInstant ObservedAt { get; }
    public IncidentPoints Points { get; }
    public CounterEpoch CounterEpoch { get; }
    public LapNumber? Lap { get; }
    public LapDistance? LapDistance { get; }
    public IncidentReviewStatus ReviewStatus { get; }
    public IncidentAnnotation Annotation { get; }

    public static ReviewIncident Create(
        IncidentId id,
        SessionIdentity session,
        ReplayPosition position,
        UtcInstant observedAt,
        IncidentPoints points,
        CounterEpoch counterEpoch,
        LapNumber? lap,
        LapDistance? lapDistance,
        IncidentReviewStatus reviewStatus,
        IncidentAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(observedAt);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(counterEpoch);
        ArgumentNullException.ThrowIfNull(reviewStatus);
        ArgumentNullException.ThrowIfNull(annotation);
        return new ReviewIncident(
            id,
            session,
            position,
            observedAt,
            points,
            counterEpoch,
            lap,
            lapDistance,
            reviewStatus,
            annotation);
    }
}
