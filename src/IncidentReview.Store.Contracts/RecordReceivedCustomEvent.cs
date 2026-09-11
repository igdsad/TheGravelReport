using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically records one remotely received event as already synchronized.</summary>
/// <remarks>
/// Implementations return <see cref="StoreErrors.CustomEventIdentityConflict"/> when the
/// deterministic identity already exists, without replacing its first committed payload.
/// </remarks>
public sealed record RecordReceivedCustomEvent : IStoreCommand
{
    private RecordReceivedCustomEvent(OperationId operationId, StoredCustomEvent customEvent)
    {
        OperationId = operationId;
        CustomEvent = customEvent;
    }

    public OperationId OperationId { get; }

    public StoredCustomEvent CustomEvent { get; }

    /// <summary>Creates a command whose synchronized event is committed in one transaction.</summary>
    public static Result<RecordReceivedCustomEvent> TryCreate(StoredCustomEvent? customEvent)
    {
        if (customEvent?.Synchronization is not CustomEventSynchronization.Synchronized)
        {
            return Result<RecordReceivedCustomEvent>.Failure(StoreErrors.InvalidCommand);
        }

        return Result<RecordReceivedCustomEvent>.Success(
            new RecordReceivedCustomEvent(OperationId.Create(), customEvent));
    }
}
