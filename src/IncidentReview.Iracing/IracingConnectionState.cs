namespace IncidentReview.Iracing;

internal sealed class IracingConnectionState
{
    private int _isAvailable;

    public IracingConnectionState(bool isAvailable = false)
    {
        _isAvailable = isAvailable ? 1 : 0;
    }

    public bool IsAvailable => Volatile.Read(ref _isAvailable) != 0;

    public void SetAvailable() => Volatile.Write(ref _isAvailable, 1);

    public void SetUnavailable() => Volatile.Write(ref _isAvailable, 0);
}
