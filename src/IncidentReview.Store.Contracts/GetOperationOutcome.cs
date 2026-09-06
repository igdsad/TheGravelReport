namespace IncidentReview.Store.Contracts;

/// <summary>
/// Requests the durable outcome of one previously submitted operation.
/// </summary>
public sealed record GetOperationOutcome : IStoreQuery<OperationOutcome>
{
    /// <summary>
    /// Initializes a reconciliation query.
    /// </summary>
    public GetOperationOutcome(OperationId operationId)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        OperationId = operationId;
    }

    /// <summary>
    /// Gets the operation to reconcile.
    /// </summary>
    public OperationId OperationId { get; }
}
