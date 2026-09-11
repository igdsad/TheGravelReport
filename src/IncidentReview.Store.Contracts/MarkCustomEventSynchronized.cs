using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically removes one custom event from the durable local outbox.</summary>
public sealed record MarkCustomEventSynchronized : IStoreCommand
{
    private MarkCustomEventSynchronized(
        OperationId operationId,
        CustomEventId customEvent,
        UtcInstant synchronizedAt)
    {
        OperationId = operationId;
        CustomEvent = customEvent;
        SynchronizedAt = synchronizedAt;
    }

    public OperationId OperationId { get; }
    public CustomEventId CustomEvent { get; }
    public UtcInstant SynchronizedAt { get; }

    /// <summary>Creates a command whose generated identity is retained for retries.</summary>
    public static MarkCustomEventSynchronized Create(
        CustomEventId customEvent,
        UtcInstant synchronizedAt)
    {
        ArgumentNullException.ThrowIfNull(customEvent);
        ArgumentNullException.ThrowIfNull(synchronizedAt);
        return new MarkCustomEventSynchronized(
            OperationId.Create(),
            customEvent,
            synchronizedAt);
    }
}
