using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>
/// Prepares and validates a store before ordinary operations are admitted.
/// </summary>
public interface IStoreInitializer
{
    /// <summary>
    /// Initializes the store and opens its operation gate on success.
    /// </summary>
    public Task<Result> InitializeAsync(CancellationToken cancellationToken);
}
