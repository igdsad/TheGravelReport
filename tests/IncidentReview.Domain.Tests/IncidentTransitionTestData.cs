namespace IncidentReview.Domain.Tests;

internal static class IncidentTransitionTestData
{
    public static SessionIdentity FirstSession { get; } = SessionIdentity.TryParse(
        "018f0c24-7a35-7a0d-8000-000000000101").Value;

    public static SessionIdentity SecondSession { get; } = SessionIdentity.TryParse(
        "018f0c24-7a35-7a0d-8000-000000000102").Value;

    public static IncidentObservation Observation(
        SessionIdentity? session = null,
        int counter = 0,
        int replaySessionNumber = 0,
        long sessionTimeMilliseconds = 1_000,
        long observedAtUnixMilliseconds = 1_800_000_000_000,
        int? lap = null,
        double? lapDistance = null)
    {
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(replaySessionNumber).Value,
            SessionTime.TryCreateMilliseconds(sessionTimeMilliseconds).Value).Value;

        return IncidentObservation.TryCreate(
            session ?? FirstSession,
            position,
            IncidentCounter.TryCreate(counter).Value,
            lap.HasValue ? LapNumber.TryCreate(lap.Value).Value : null,
            lapDistance.HasValue ? LapDistance.TryCreate(lapDistance.Value).Value : null,
            UtcInstant.TryCreateUnixMilliseconds(observedAtUnixMilliseconds).Value).Value;
    }

    public static IncidentCheckpoint Checkpoint(
        SessionIdentity? session = null,
        int epoch = 0,
        int counter = 0,
        int replaySessionNumber = 0,
        long sessionTimeMilliseconds = 1_000,
        long updatedAtUnixMilliseconds = 1_800_000_000_000)
    {
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(replaySessionNumber).Value,
            SessionTime.TryCreateMilliseconds(sessionTimeMilliseconds).Value).Value;

        return IncidentCheckpoint.TryCreate(
            session ?? FirstSession,
            CounterEpoch.TryCreate(epoch).Value,
            IncidentCounter.TryCreate(counter).Value,
            position,
            UtcInstant.TryCreateUnixMilliseconds(updatedAtUnixMilliseconds).Value).Value;
    }
}
