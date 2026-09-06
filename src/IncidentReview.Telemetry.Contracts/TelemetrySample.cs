using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Contains one validated, simulator-neutral telemetry observation.
/// </summary>
public sealed record TelemetrySample
{
    /// <summary>Gets the maximum number of participant counters in one sample.</summary>
    public const int MaximumIncidentCounterCount = 256;

    private static readonly Error InvalidSampleError = Error.Create(
        TelemetryErrorCodes.InvalidSample,
        ErrorKind.Validation,
        "A telemetry sample requires consistent validated session, position, participant counters, state, and time values.");

    private TelemetrySample(
        SimulatorSessionDescriptor session,
        ReplayPosition position,
        IReadOnlyList<ParticipantIncidentCounter> incidentCounters,
        OnTrackState onTrackState,
        UtcInstant observedAt)
    {
        Session = session;
        Position = position;
        IncidentCounters = incidentCounters;
        OnTrackState = onTrackState;
        ObservedAt = observedAt;
    }

    /// <summary>Gets the simulator-owned session identity evidence.</summary>
    public SimulatorSessionDescriptor Session { get; }

    /// <summary>Gets the simulator-neutral replay position.</summary>
    public ReplayPosition Position { get; }

    /// <summary>
    /// Gets participant counters in the deterministic order supplied by the telemetry source.
    /// </summary>
    public IReadOnlyList<ParticipantIncidentCounter> IncidentCounters { get; }

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
        IEnumerable<ParticipantIncidentCounter>? incidentCounters,
        OnTrackState onTrackState,
        UtcInstant? observedAt)
    {
        if (session is null ||
            position is null ||
            incidentCounters is null ||
            observedAt is null ||
            !Enum.IsDefined(onTrackState) ||
            session.SessionNumber != position.SessionNumber)
        {
            return Result<TelemetrySample>.Failure(InvalidSampleError);
        }

        var copiedCounters = new List<ParticipantIncidentCounter>();
        var identities = new HashSet<ParticipantIdentity>();
        foreach (var counter in incidentCounters)
        {
            if (counter is null ||
                copiedCounters.Count == MaximumIncidentCounterCount ||
                !identities.Add(counter.Participant.Identity))
            {
                return Result<TelemetrySample>.Failure(InvalidSampleError);
            }

            copiedCounters.Add(counter);
        }

        return Result<TelemetrySample>.Success(new TelemetrySample(
            session,
            position,
            Array.AsReadOnly(copiedCounters.ToArray()),
            onTrackState,
            observedAt));
    }
}
