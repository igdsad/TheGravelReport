using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Contains one validated, simulator-neutral incident-counter observation.</summary>
public sealed record IncidentObservation
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-observation.invalid",
        "An incident observation requires session, replay position, counter, and UTC time values.");

    private IncidentObservation(
        SessionIdentity session,
        ReplayPosition position,
        IncidentCounter incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance,
        UtcInstant observedAt)
    {
        Session = session;
        Position = position;
        IncidentCounter = incidentCounter;
        Lap = lap;
        LapDistance = lapDistance;
        ObservedAt = observedAt;
    }

    /// <summary>Gets the resolved application session identity.</summary>
    public SessionIdentity Session { get; }

    /// <summary>Gets the replay position where this counter value was first observed.</summary>
    public ReplayPosition Position { get; }

    /// <summary>Gets the cumulative incident counter.</summary>
    public IncidentCounter IncidentCounter { get; }

    /// <summary>Gets the optional lap number reported with the observation.</summary>
    public LapNumber? Lap { get; }

    /// <summary>Gets the optional normalized lap distance.</summary>
    public LapDistance? LapDistance { get; }

    /// <summary>Gets when the observation was captured.</summary>
    public UtcInstant ObservedAt { get; }

    /// <summary>Combines validated observation values.</summary>
    public static Result<IncidentObservation> TryCreate(
        SessionIdentity? session,
        ReplayPosition? position,
        IncidentCounter? incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance,
        UtcInstant? observedAt) =>
        session is not null &&
        position is not null &&
        incidentCounter is not null &&
        observedAt is not null
            ? Result<IncidentObservation>.Success(
                new IncidentObservation(
                    session,
                    position,
                    incidentCounter,
                    lap,
                    lapDistance,
                    observedAt))
            : Result<IncidentObservation>.Failure(InvalidError);
}
