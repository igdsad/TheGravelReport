namespace IncidentReview.Store.Contracts;

/// <summary>
/// Marks an immutable request for one complete store snapshot.
/// </summary>
/// <typeparam name="T">The non-null query result.</typeparam>
public interface IStoreQuery<out T>
    where T : notnull
{
}
