using IncidentReview.Iracing.Protocol;

namespace IncidentReview.Iracing;

internal sealed class IracingConnectionState
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = CreateSignal();
    private ReplayFrameObservation _observation = new ReplayFrameObservation.Unavailable(0);
    private string? _metadataSource;
    private IracingReplayContextMetadata _metadata = IracingReplayContextMetadata.Empty;

    public ReplayFrameObservation Read()
    {
        lock (_lock)
        {
            return _observation;
        }
    }

    public void PublishAvailable(IracingFrameSnapshot snapshot)
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

            _observation = new ReplayFrameObservation.Available(
                checked(_observation.Version + 1),
                DecodeReplayFrame(snapshot, _metadata));
            signal = RotateSignal();
        }

        signal.TrySetResult();
    }

    public void PublishAvailable(ReplayFrameState frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        TaskCompletionSource signal;
        lock (_lock)
        {
            _observation = new ReplayFrameObservation.Available(
                checked(_observation.Version + 1),
                frame);
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
            if (_observation.Version != observedVersion)
            {
                return;
            }

            wait = _changed.Task;
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void PublishUnavailable()
    {
        TaskCompletionSource? signal = null;
        lock (_lock)
        {
            if (_observation is ReplayFrameObservation.Available)
            {
                _observation = new ReplayFrameObservation.Unavailable(
                    checked(_observation.Version + 1));
                _metadataSource = null;
                _metadata = IracingReplayContextMetadata.Empty;
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

internal abstract record ReplayFrameObservation
{
    private ReplayFrameObservation(long version)
    {
        Version = version;
    }

    public long Version { get; }

    internal sealed record Unavailable : ReplayFrameObservation
    {
        public Unavailable(long version)
            : base(version)
        {
        }
    }

    internal sealed record Available : ReplayFrameObservation
    {
        public Available(long version, ReplayFrameState frame)
            : base(version)
        {
            Frame = frame;
        }

        public ReplayFrameState Frame { get; }
    }
}

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
