using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically establishes or advances a baseline checkpoint.</summary>
public sealed record EstablishIncidentCheckpoint : IStoreCommand
{
    private EstablishIncidentCheckpoint(
        OperationId operationId,
        IncidentCheckpoint? expectedCheckpoint,
        IncidentCheckpoint nextCheckpoint)
    {
        OperationId = operationId;
        ExpectedCheckpoint = expectedCheckpoint;
        NextCheckpoint = nextCheckpoint;
    }

    public OperationId OperationId { get; }
    public IncidentCheckpoint? ExpectedCheckpoint { get; }
    public IncidentCheckpoint NextCheckpoint { get; }

    public static Result<EstablishIncidentCheckpoint> TryCreate(
        IncidentCheckpoint? expectedCheckpoint,
        IncidentCheckpoint? nextCheckpoint)
    {
        if (nextCheckpoint is null ||
            (expectedCheckpoint is null && nextCheckpoint.CounterEpoch.Value != 0) ||
            (expectedCheckpoint is not null &&
             (expectedCheckpoint.Session != nextCheckpoint.Session ||
              expectedCheckpoint.ParticipantIdentity != nextCheckpoint.ParticipantIdentity ||
              expectedCheckpoint.CounterEpoch.Value == int.MaxValue ||
              nextCheckpoint.CounterEpoch.Value != expectedCheckpoint.CounterEpoch.Value + 1 ||
              (nextCheckpoint.LastPosition.SessionNumber ==
                   expectedCheckpoint.LastPosition.SessionNumber &&
               nextCheckpoint.LastCounter.Value >= expectedCheckpoint.LastCounter.Value))))
        {
            return Result<EstablishIncidentCheckpoint>.Failure(StoreErrors.InvalidCommand);
        }

        return Result<EstablishIncidentCheckpoint>.Success(
            new EstablishIncidentCheckpoint(OperationId.Create(), expectedCheckpoint, nextCheckpoint));
    }
}
