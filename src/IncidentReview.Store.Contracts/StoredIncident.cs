using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>A complete simulator-neutral durable incident record.</summary>
public sealed record StoredIncident
{
    private StoredIncident(
        IncidentId id,
        SessionIdentity session,
        ReplayPosition position,
        UtcInstant observedAt,
        IncidentPoints points,
        CounterEpoch counterEpoch,
        LapNumber? lap,
        LapDistance? lapDistance,
        IncidentReviewStatus reviewStatus,
        IncidentAnnotation annotation,
        UtcInstant createdAt,
        UtcInstant updatedAt)
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
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
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
    public UtcInstant CreatedAt { get; }
    public UtcInstant UpdatedAt { get; }

    public static StoredIncident Create(
        IncidentId id,
        SessionIdentity session,
        ReplayPosition position,
        UtcInstant observedAt,
        IncidentPoints points,
        CounterEpoch counterEpoch,
        LapNumber? lap,
        LapDistance? lapDistance,
        IncidentReviewStatus reviewStatus,
        IncidentAnnotation annotation,
        UtcInstant createdAt,
        UtcInstant updatedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(observedAt);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(counterEpoch);
        ArgumentNullException.ThrowIfNull(reviewStatus);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(createdAt);
        ArgumentNullException.ThrowIfNull(updatedAt);
        if (updatedAt.UnixMilliseconds < createdAt.UnixMilliseconds)
        {
            throw new ArgumentException("The updated time cannot precede the created time.", nameof(updatedAt));
        }

        return new StoredIncident(
            id,
            session,
            position,
            observedAt,
            points,
            counterEpoch,
            lap,
            lapDistance,
            reviewStatus,
            annotation,
            createdAt,
            updatedAt);
    }
}
