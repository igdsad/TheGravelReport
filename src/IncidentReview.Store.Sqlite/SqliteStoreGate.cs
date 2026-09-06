namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteStoreGate
{
    private int _isOpen;

    public bool IsOpen => Volatile.Read(ref _isOpen) == 1;

    public void Open() => Interlocked.Exchange(ref _isOpen, 1);
}
