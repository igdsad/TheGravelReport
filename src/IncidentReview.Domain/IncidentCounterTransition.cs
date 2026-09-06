using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Evaluates deterministic incident-counter state transitions.</summary>
public static class IncidentCounterTransition
{
    private static readonly Error InvalidObservationError = DomainValidationError.Create(
        "domain.incident-transition.invalid-observation",
        "An incident-counter transition requires a validated observation.");

    private static readonly Error SessionNumberMismatchError = Error.Create(
        ErrorCode.Define("domain.incident-transition.session-number-mismatch"),
        ErrorKind.Conflict,
        "The same application session cannot change replay session number.");

    private static readonly Error ParticipantMismatchError = Error.Create(
        ErrorCode.Define("domain.incident-transition.participant-mismatch"),
        ErrorKind.Conflict,
        "An incident checkpoint cannot be evaluated against another participant.");

    private static readonly Error CounterEpochOverflowError = Error.Create(
        ErrorCode.Define("domain.incident-transition.counter-epoch-overflow"),
        ErrorKind.Conflict,
        "The incident counter epoch cannot advance beyond its supported range.");

    /// <summary>
    /// Evaluates one ordered observation against the last durably accepted checkpoint.
    /// </summary>
    public static Result<IncidentTransitionDecision> Evaluate(
        IncidentCheckpoint? checkpoint,
        IncidentObservation? observation)
    {
        if (observation is null)
        {
            return Result<IncidentTransitionDecision>.Failure(InvalidObservationError);
        }

        if (checkpoint is null)
        {
            return EstablishBaseline(observation, IncidentBaselineReason.Initial, epoch: 0);
        }

        if (checkpoint.Session != observation.Session)
        {
            return EstablishBaseline(
                observation,
                IncidentBaselineReason.SessionChanged,
                epoch: 0);
        }

        if (checkpoint.ParticipantIdentity != observation.Participant.Identity)
        {
            return Result<IncidentTransitionDecision>.Failure(ParticipantMismatchError);
        }

        if (checkpoint.LastPosition.SessionNumber != observation.Position.SessionNumber)
        {
            return Result<IncidentTransitionDecision>.Failure(SessionNumberMismatchError);
        }

        if (checkpoint.LastCounter == observation.IncidentCounter)
        {
            return Result<IncidentTransitionDecision>.Success(
                IncidentTransitionDecision.NoChange.Instance);
        }

        if (observation.IncidentCounter.Value > checkpoint.LastCounter.Value)
        {
            return RecordIncrease(checkpoint, observation);
        }

        if (checkpoint.CounterEpoch.Value == int.MaxValue)
        {
            return Result<IncidentTransitionDecision>.Failure(CounterEpochOverflowError);
        }

        return EstablishBaseline(
            observation,
            IncidentBaselineReason.CounterReset,
            checkpoint.CounterEpoch.Value + 1);
    }

    private static Result<IncidentTransitionDecision> EstablishBaseline(
        IncidentObservation observation,
        IncidentBaselineReason reason,
        int epoch)
    {
        var nextCheckpoint = CreateCheckpoint(
            observation,
            CounterEpoch.TryCreate(epoch).Value);

        return Result<IncidentTransitionDecision>.Success(
            IncidentTransitionDecision.EstablishBaseline.Create(
                reason,
                nextCheckpoint));
    }

    private static Result<IncidentTransitionDecision> RecordIncrease(
        IncidentCheckpoint checkpoint,
        IncidentObservation observation)
    {
        var points = IncidentPoints.TryCreate(
            observation.IncidentCounter.Value,
            observation.IncidentCounter.Value - checkpoint.LastCounter.Value).Value;
        var nextCheckpoint = CreateCheckpoint(observation, checkpoint.CounterEpoch);

        return Result<IncidentTransitionDecision>.Success(
            IncidentTransitionDecision.RecordIncrease.Create(
                points,
                checkpoint,
                nextCheckpoint,
                observation));
    }

    private static IncidentCheckpoint CreateCheckpoint(
        IncidentObservation observation,
        CounterEpoch epoch) => IncidentCheckpoint.TryCreate(
            observation.Session,
            observation.Participant.Identity,
            epoch,
            observation.IncidentCounter,
            observation.Position,
            observation.ObservedAt).Value;
}
