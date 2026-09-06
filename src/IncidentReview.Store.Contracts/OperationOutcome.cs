namespace IncidentReview.Store.Contracts;

/// <summary>
/// The durable result of reconciling an operation whose commit was indeterminate.
/// </summary>
public sealed record OperationOutcome
{
    private OperationOutcome(bool isCommitted)
    {
        IsCommitted = isCommitted;
    }

    /// <summary>
    /// Gets the outcome used when the operation record exists in the validated store.
    /// </summary>
    public static OperationOutcome Committed { get; } = new(isCommitted: true);

    /// <summary>
    /// Gets the outcome used when the validated store proves the operation record absent.
    /// </summary>
    public static OperationOutcome NotCommitted { get; } = new(isCommitted: false);

    /// <summary>
    /// Gets whether the operation is durably recorded as committed.
    /// </summary>
    public bool IsCommitted { get; }
}
