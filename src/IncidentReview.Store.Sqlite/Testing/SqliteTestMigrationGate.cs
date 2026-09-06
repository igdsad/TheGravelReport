namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Coordinates a deterministic pause from inside a test-only SQLite migration.</summary>
public sealed class SqliteTestMigrationGate : IDisposable
{
    private readonly ManualResetEventSlim _entered = new(initialState: false);
    private readonly ManualResetEventSlim _release = new(initialState: false);
    private int _isDisposed;

    /// <summary>Waits until DbUp is executing the test-only gate function.</summary>
    public bool WaitUntilEntered(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return _entered.Wait(timeout);
    }

    /// <summary>Allows the paused migration to finish.</summary>
    public void Release()
    {
        ThrowIfDisposed();
        _release.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _release.Set();
        _entered.Dispose();
        _release.Dispose();
    }

    internal long EnterAndWait()
    {
        ThrowIfDisposed();
        _entered.Set();
        _release.Wait();
        return 1;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
