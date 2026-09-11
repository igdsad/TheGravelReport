using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically records one custom event in the durable local outbox.</summary>
public sealed record RecordCustomEvent : IStoreCommand
{
    private RecordCustomEvent(OperationId operationId, StoredCustomEvent customEvent)
    {
        OperationId = operationId;
        CustomEvent = customEvent;
    }

    public OperationId OperationId { get; }
    public StoredCustomEvent CustomEvent { get; }

    /// <summary>Creates a command for an event that has not yet been synchronized.</summary>
    public static Result<RecordCustomEvent> TryCreate(StoredCustomEvent? customEvent)
    {
        if (customEvent?.Synchronization is not CustomEventSynchronization.Pending)
        {
            return Result<RecordCustomEvent>.Failure(StoreErrors.InvalidCommand);
        }

        return Result<RecordCustomEvent>.Success(
            new RecordCustomEvent(OperationId.Create(), customEvent));
    }
}
