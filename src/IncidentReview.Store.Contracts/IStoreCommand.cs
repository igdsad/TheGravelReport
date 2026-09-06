namespace IncidentReview.Store.Contracts;

/// <summary>
/// Marks an immutable application mutation with a stable retry identity.
/// </summary>
public interface IStoreCommand
{
    /// <summary>
    /// Gets the identity used to provide at-most-once committed effects.
    /// </summary>
    public OperationId OperationId { get; }
}
