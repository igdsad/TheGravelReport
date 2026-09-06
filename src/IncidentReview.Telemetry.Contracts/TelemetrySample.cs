using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Contains one validated, simulator-neutral telemetry observation.
/// </summary>
public sealed record TelemetrySample
{
    private static readonly Error InvalidSampleError = Error.Create(
        TelemetryErrorCodes.InvalidSample,
        ErrorKind.Validation,
        "A telemetry sample requires consistent validated session, position, counter, state, and time values.");

    private TelemetrySample(
        SimulatorSessionDescriptor session,
        ReplayPosition position,
        IncidentCounter incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance,
        OnTrackState onTrackState,
        UtcInstant observedAt)
    {
        Session = session;
        Position = position;
        IncidentCounter = incidentCounter;
        Lap = lap;
        LapDistance = lapDistance;
        OnTrackState = onTrackState;
        ObservedAt = observedAt;
    }

    /// <summary>Gets the simulator-owned session identity evidence.</summary>
    public SimulatorSessionDescriptor Session { get; }

    /// <summary>Gets the simulator-neutral replay position.</summary>
    public ReplayPosition Position { get; }

    /// <summary>Gets the cumulative incident counter.</summary>
    public IncidentCounter IncidentCounter { get; }

    /// <summary>Gets the current lap when the source can determine it.</summary>
    public LapNumber? Lap { get; }

    /// <summary>Gets normalized lap progress when the source can determine it.</summary>
    public LapDistance? LapDistance { get; }

    /// <summary>Gets whether the observed participant is actively on track.</summary>
    public OnTrackState OnTrackState { get; }

    /// <summary>Gets when this sample was copied into application-owned memory.</summary>
    public UtcInstant ObservedAt { get; }

    /// <summary>
    /// Combines validated values while enforcing cross-value session consistency.
    /// </summary>
    public static Result<TelemetrySample> TryCreate(
        SimulatorSessionDescriptor? session,
        ReplayPosition? position,
        IncidentCounter? incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance,
        OnTrackState onTrackState,
        UtcInstant? observedAt)
    {
        if (session is null ||
            position is null ||
            incidentCounter is null ||
            observedAt is null ||
            !Enum.IsDefined(onTrackState) ||
            session.SessionNumber != position.SessionNumber)
        {
            return Result<TelemetrySample>.Failure(InvalidSampleError);
        }

        return Result<TelemetrySample>.Success(new TelemetrySample(
            session,
            position,
            incidentCounter,
            lap,
            lapDistance,
            onTrackState,
            observedAt));
    }
}
