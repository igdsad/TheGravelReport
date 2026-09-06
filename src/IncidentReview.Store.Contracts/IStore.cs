using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>
/// Provides snapshot queries and atomic, idempotent application mutations.
/// </summary>
public interface IStore
{
    /// <summary>
    /// Executes one typed query against a consistent store snapshot.
    /// </summary>
    public Task<Result<T>> QueryAsync<T>(
        IStoreQuery<T> query,
        CancellationToken cancellationToken)
        where T : notnull;

    /// <summary>
    /// Executes one command as a single atomic operation.
    /// </summary>
    public Task<Result> ExecuteAsync(
        IStoreCommand command,
        CancellationToken cancellationToken);
}
