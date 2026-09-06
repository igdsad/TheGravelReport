using IncidentReview.Store.Contracts;

namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Owns a store and its paired startup gate for real-database tests.</summary>
public sealed class SqliteTestStoreContext : IAsyncDisposable
{
    private readonly IAsyncDisposable _owner;

    internal SqliteTestStoreContext(
        IStore store,
        IStoreInitializer initializer,
        IAsyncDisposable owner)
    {
        Store = store;
        Initializer = initializer;
        _owner = owner;
    }

    /// <summary>Gets the store contract under test.</summary>
    public IStore Store { get; }

    /// <summary>Gets the initializer that controls the paired store gate.</summary>
    public IStoreInitializer Initializer { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _owner.DisposeAsync();
}
