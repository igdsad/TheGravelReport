using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Contains one participant's cumulative incident counter and optional track position.
/// </summary>
public sealed record ParticipantIncidentCounter
{
    private static readonly Error InvalidError = Error.Create(
        TelemetryErrorCodes.InvalidSample,
        ErrorKind.Validation,
        "A participant incident counter requires validated participant and counter values.");

    private ParticipantIncidentCounter(
        IncidentParticipant participant,
        IncidentCounter incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance)
    {
        Participant = participant;
        IncidentCounter = incidentCounter;
        Lap = lap;
        LapDistance = lapDistance;
    }

    /// <summary>Gets the participant represented by this counter.</summary>
    public IncidentParticipant Participant { get; }

    /// <summary>Gets the participant's cumulative scored incident count.</summary>
    public IncidentCounter IncidentCounter { get; }

    /// <summary>Gets the participant's current lap when the source can determine it.</summary>
    public LapNumber? Lap { get; }

    /// <summary>Gets the participant's normalized lap progress when available.</summary>
    public LapDistance? LapDistance { get; }

    /// <summary>Combines one participant's validated incident-counter context.</summary>
    public static Result<ParticipantIncidentCounter> TryCreate(
        IncidentParticipant? participant,
        IncidentCounter? incidentCounter,
        LapNumber? lap,
        LapDistance? lapDistance) =>
        participant is not null && incidentCounter is not null
            ? Result<ParticipantIncidentCounter>.Success(
                new ParticipantIncidentCounter(
                    participant,
                    incidentCounter,
                    lap,
                    lapDistance))
            : Result<ParticipantIncidentCounter>.Failure(InvalidError);
}
