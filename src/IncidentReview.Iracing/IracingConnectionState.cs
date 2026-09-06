using IncidentReview.Iracing.Protocol;

namespace IncidentReview.Iracing;

internal sealed class IracingConnectionState
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = CreateSignal();
    private ReplayFrameState? _latestFrame;
    private string? _metadataSource;
    private IracingReplayContextMetadata _metadata = IracingReplayContextMetadata.Empty;
    private long _version;
    private bool _isAvailable;

    public IracingConnectionState(bool isAvailable = false)
    {
        _isAvailable = isAvailable;
    }

    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _isAvailable;
            }
        }
    }

    public ReplayFrameObservation Read()
    {
        lock (_lock)
        {
            return new ReplayFrameObservation(_version, _isAvailable, _latestFrame);
        }
    }

    public void ObserveStableFrame(IracingFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        TaskCompletionSource signal;
        lock (_lock)
        {
            if (!string.Equals(_metadataSource, snapshot.SessionInfo, StringComparison.Ordinal))
            {
                _metadataSource = snapshot.SessionInfo;
                _metadata = IracingSessionInfoDecoder.ReadReplayContextMetadata(
                    snapshot.SessionInfo);
            }

            _latestFrame = DecodeReplayFrame(snapshot, _metadata);
            _version++;
            signal = RotateSignal();
        }

        signal.TrySetResult();
    }

    public void ObserveTestFrame(ReplayFrameState frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        TaskCompletionSource signal;
        lock (_lock)
        {
            _latestFrame = frame;
            _version++;
            signal = RotateSignal();
        }

        signal.TrySetResult();
    }

    public async ValueTask WaitForChangeAsync(
        long observedVersion,
        CancellationToken cancellationToken)
    {
        Task wait;
        lock (_lock)
        {
            if (_version != observedVersion)
            {
                return;
            }

            wait = _changed.Task;
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetAvailable()
    {
        TaskCompletionSource? signal = null;
        lock (_lock)
        {
            if (!_isAvailable)
            {
                _isAvailable = true;
                _version++;
                signal = RotateSignal();
            }
        }

        signal?.TrySetResult();
    }

    public void SetUnavailable()
    {
        TaskCompletionSource? signal = null;
        lock (_lock)
        {
            if (_isAvailable || _latestFrame is not null)
            {
                _isAvailable = false;
                _latestFrame = null;
                _metadataSource = null;
                _metadata = IracingReplayContextMetadata.Empty;
                _version++;
                signal = RotateSignal();
            }
        }

        signal?.TrySetResult();
    }

    private TaskCompletionSource RotateSignal()
    {
        var signal = _changed;
        _changed = CreateSignal();
        return signal;
    }

    private static ReplayFrameState DecodeReplayFrame(
        IracingFrameSnapshot snapshot,
        IracingReplayContextMetadata metadata)
    {
        int? sessionNumber = IracingFrameDecoder.TryReadInt32(
            snapshot,
            IracingProtocol.ReplaySessionNumberVariable,
            out var sessionNumberValue)
            ? sessionNumberValue
            : null;
        long? sessionMilliseconds = IracingFrameDecoder.TryReadDouble(
                snapshot,
                IracingProtocol.ReplaySessionTimeVariable,
                out var sessionSeconds) &&
            IracingFrameDecoder.TryConvertSessionMilliseconds(
                sessionSeconds,
                out var sessionMillisecondsValue)
                ? sessionMillisecondsValue
                : null;
        int? playSpeed = IracingFrameDecoder.TryReadInt32(
            snapshot,
            IracingProtocol.ReplayPlaySpeedVariable,
            out var playSpeedValue)
            ? playSpeedValue
            : null;
        bool? slowMotion = IracingFrameDecoder.TryReadBoolean(
            snapshot,
            IracingProtocol.ReplayPlaySlowMotionVariable,
            out var slowMotionValue)
            ? slowMotionValue
            : null;
        int? cameraState = IracingFrameDecoder.TryReadBitField(
            snapshot,
            IracingProtocol.CameraStateVariable,
            out var cameraStateValue)
            ? cameraStateValue
            : null;
        int? cameraCarIndex = IracingFrameDecoder.TryReadInt32(
            snapshot,
            IracingProtocol.CameraCarIndexVariable,
            out var cameraCarIndexValue)
            ? cameraCarIndexValue
            : null;
        int? cameraGroupNumber = IracingFrameDecoder.TryReadInt32(
            snapshot,
            IracingProtocol.CameraGroupNumberVariable,
            out var cameraGroupNumberValue)
            ? cameraGroupNumberValue
            : null;
        int? cameraNumber = IracingFrameDecoder.TryReadInt32(
            snapshot,
            IracingProtocol.CameraNumberVariable,
            out var cameraNumberValue)
            ? cameraNumberValue
            : null;

        return new ReplayFrameState(
            sessionNumber,
            sessionMilliseconds,
            playSpeed,
            slowMotion,
            cameraState,
            cameraCarIndex,
            cameraGroupNumber,
            cameraNumber,
            metadata);
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record ReplayFrameObservation(
    long Version,
    bool IsAvailable,
    ReplayFrameState? Frame);

internal sealed record ReplayFrameState(
    int? ReplaySessionNumber,
    long? ReplaySessionTimeMilliseconds,
    int? ReplayPlaySpeed,
    bool? ReplayPlaySlowMotion,
    int? CameraState,
    int? CameraCarIndex,
    int? CameraGroupNumber,
    int? CameraNumber,
    IracingReplayContextMetadata Metadata)
{
    public bool IsSessionScreen =>
        (CameraState & (int)IracingCameraState.IsSessionScreen) != 0;
}
