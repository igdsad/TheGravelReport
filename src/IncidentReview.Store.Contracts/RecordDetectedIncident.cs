using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically inserts an incident and advances its detector checkpoint.</summary>
public sealed record RecordDetectedIncident : IStoreCommand
{
    private RecordDetectedIncident(
        OperationId operationId,
        StoredIncident incident,
        IncidentCheckpoint expectedCheckpoint,
        IncidentCheckpoint nextCheckpoint)
    {
        OperationId = operationId;
        Incident = incident;
        ExpectedCheckpoint = expectedCheckpoint;
        NextCheckpoint = nextCheckpoint;
    }

    public OperationId OperationId { get; }
    public StoredIncident Incident { get; }
    public IncidentCheckpoint ExpectedCheckpoint { get; }
    public IncidentCheckpoint NextCheckpoint { get; }

    public static Result<RecordDetectedIncident> TryCreate(
        StoredIncident? incident,
        IncidentCheckpoint? expectedCheckpoint,
        IncidentCheckpoint? nextCheckpoint)
    {
        if (incident is null || expectedCheckpoint is null || nextCheckpoint is null ||
            incident.Session != expectedCheckpoint.Session ||
            nextCheckpoint.Session != expectedCheckpoint.Session ||
            nextCheckpoint.CounterEpoch != expectedCheckpoint.CounterEpoch ||
            nextCheckpoint.LastCounter.Value <= expectedCheckpoint.LastCounter.Value ||
            nextCheckpoint.LastPosition.SessionNumber != expectedCheckpoint.LastPosition.SessionNumber ||
            incident.CounterEpoch != nextCheckpoint.CounterEpoch ||
            incident.Points.Total != nextCheckpoint.LastCounter.Value ||
            incident.Points.Delta !=
                nextCheckpoint.LastCounter.Value - expectedCheckpoint.LastCounter.Value ||
            incident.Position != nextCheckpoint.LastPosition ||
            incident.ObservedAt != nextCheckpoint.UpdatedAt)
        {
            return Result<RecordDetectedIncident>.Failure(StoreErrors.InvalidCommand);
        }

        return Result<RecordDetectedIncident>.Success(
            new RecordDetectedIncident(
                OperationId.Create(),
                incident,
                expectedCheckpoint,
                nextCheckpoint));
    }
}
