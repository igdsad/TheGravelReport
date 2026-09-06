using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Captures durable progress for one session's incident counter.</summary>
public sealed record IncidentCheckpoint
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-checkpoint.invalid",
        "An incident checkpoint requires session, participant identity, epoch, counter, replay position, and UTC time values.");

    private IncidentCheckpoint(
        SessionIdentity session,
        ParticipantIdentity participantIdentity,
        CounterEpoch counterEpoch,
        IncidentCounter lastCounter,
        ReplayPosition lastPosition,
        UtcInstant updatedAt)
    {
        Session = session;
        ParticipantIdentity = participantIdentity;
        CounterEpoch = counterEpoch;
        LastCounter = lastCounter;
        LastPosition = lastPosition;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the application session whose progress is captured.</summary>
    public SessionIdentity Session { get; }

    /// <summary>Gets the participant counter whose progress is captured.</summary>
    public ParticipantIdentity ParticipantIdentity { get; }

    /// <summary>Gets the continuity epoch containing the last counter.</summary>
    public CounterEpoch CounterEpoch { get; }

    /// <summary>Gets the last durably accepted cumulative counter.</summary>
    public IncidentCounter LastCounter { get; }

    /// <summary>Gets the replay position of the last durably accepted observation.</summary>
    public ReplayPosition LastPosition { get; }

    /// <summary>Gets when the checkpoint was last updated.</summary>
    public UtcInstant UpdatedAt { get; }

    /// <summary>Combines validated durable checkpoint values.</summary>
    public static Result<IncidentCheckpoint> TryCreate(
        SessionIdentity? session,
        ParticipantIdentity? participantIdentity,
        CounterEpoch? counterEpoch,
        IncidentCounter? lastCounter,
        ReplayPosition? lastPosition,
        UtcInstant? updatedAt) =>
        session is not null &&
        participantIdentity is not null &&
        counterEpoch is not null &&
        lastCounter is not null &&
        lastPosition is not null &&
        updatedAt is not null
            ? Result<IncidentCheckpoint>.Success(
                new IncidentCheckpoint(
                    session,
                    participantIdentity,
                    counterEpoch,
                    lastCounter,
                    lastPosition,
                    updatedAt))
            : Result<IncidentCheckpoint>.Failure(InvalidError);

}
