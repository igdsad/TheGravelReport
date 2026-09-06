namespace IncidentReview.Host.Wpf;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\IncidentReview.Host.Wpf";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool isAcquired)
    {
        _mutex = mutex;
        IsAcquired = isAcquired;
    }

    public bool IsAcquired { get; }

    public static SingleInstanceGuard TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsAcquired)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
