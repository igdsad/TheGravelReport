using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using IncidentReview.Domain;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Iracing.Telemetry;
using IncidentReview.Results;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing;

internal sealed class IracingTelemetrySource : ITelemetrySource, IAsyncDisposable
{
    private static readonly Task<Exception?> NoProducer =
        Task.FromResult<Exception?>(null);

    private readonly IracingProtocolConfiguration _configuration;
    private readonly IracingConnectionState _connectionState;
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly Lock _lifetimeLock = new();
    private IracingSharedMemoryConnection? _currentConnection;
    private Task<Exception?> _producerStopped = NoProducer;
    private bool _isDisposed;
    private bool _isObserving;

    public IracingTelemetrySource(
        IracingProtocolConfiguration configuration,
        IracingConnectionState connectionState)
    {
        _configuration = configuration;
        _connectionState = connectionState;
    }

    public async IAsyncEnumerable<TelemetryEvent> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var admission = BeginObservation();
        if (admission is ObservationAdmission.Disposed)
        {
            yield break;
        }

        if (admission is ObservationAdmission.AlreadyObserving)
        {
            yield return TelemetryUnavailable.Create(IracingErrors.TelemetryAlreadyObserved);
            yield break;
        }

        var started = (ObservationAdmission.Started)admission;
        CancellationTokenSource? linkedSource = null;
        Task<Error?>? producer = null;

        try
        {
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposeSource.Token);
            var channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(
                _configuration.Options.EventBufferCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
            producer = ProduceAndSignalStoppedAsync(
                channel.Writer,
                started.ProducerStopped,
                linkedSource.Token);

            while (await WaitToReadAsync(
                       channel.Reader,
                       linkedSource.Token,
                       cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var telemetryEvent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_disposeSource.IsCancellationRequested)
                    {
                        yield break;
                    }

                    yield return telemetryEvent;
                }
            }

            if (!_disposeSource.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var terminalError = await producer.ConfigureAwait(false);
                var producerFault = await started.ProducerStopped.Task.ConfigureAwait(false);
                if (producerFault is not null)
                {
                    ExceptionDispatchInfo.Capture(producerFault).Throw();
                }

                if (terminalError is not null)
                {
                    yield return TelemetryUnavailable.Create(terminalError);
                }
            }
        }
        finally
        {
            linkedSource?.Cancel();
            try
            {
                if (producer is null)
                {
                    started.ProducerStopped.TrySetResult(null);
                }
                else
                {
                    await AwaitProducerShutdownAsync(
                        producer,
                        started.ProducerStopped.Task,
                        linkedSource!.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _connectionState.PublishUnavailable();
                EndObservation();
                linkedSource?.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<Exception?> producerStopped;
        lock (_lifetimeLock)
        {
            _isDisposed = true;
            producerStopped = _producerStopped;
        }

        _connectionState.PublishUnavailable();
        _disposeSource.Cancel();
        Interlocked.Exchange(ref _currentConnection, null)?.Dispose();
        var producerFault = await producerStopped.ConfigureAwait(false);
        _connectionState.PublishUnavailable();
        if (producerFault is not null)
        {
            ExceptionDispatchInfo.Capture(producerFault).Throw();
        }
    }

    private ObservationAdmission BeginObservation()
    {
        lock (_lifetimeLock)
        {
            if (_isDisposed)
            {
                return ObservationAdmission.Disposed.Instance;
            }

            if (_isObserving)
            {
                return ObservationAdmission.AlreadyObserving.Instance;
            }

            _isObserving = true;
            var producerStopped = new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _producerStopped = producerStopped.Task;
            return new ObservationAdmission.Started(producerStopped);
        }
    }

    private void EndObservation()
    {
        lock (_lifetimeLock)
        {
            _isObserving = false;
            _producerStopped = NoProducer;
        }
    }

    private async Task<Error?> ProduceAndSignalStoppedAsync(
        ChannelWriter<TelemetryEvent> writer,
        TaskCompletionSource<Exception?> producerStopped,
        CancellationToken cancellationToken)
    {
        Exception? fault = null;
        try
        {
            return await ProduceAsync(writer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            fault = exception;
            return null;
        }
        finally
        {
            producerStopped.TrySetResult(fault);
        }
    }

    private async Task<Error?> ProduceAsync(
        ChannelWriter<TelemetryEvent> writer,
        CancellationToken cancellationToken)
    {
        Exception? completionException = null;
        IracingSharedMemoryConnection? connection = null;
        var state = IracingTelemetryLifecycleReducer.InitialState;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var copiedStableFrame = false;
                IracingTelemetryLifecycleTransition transition;
                try
                {
                    var input = ObserveLifecycleInput(
                        state,
                        ref connection,
                        out copiedStableFrame);
                    transition = IracingTelemetryLifecycleReducer.Reduce(state, input);
                    state = transition.State;
                    var effectError = ApplyLifecycleEffects(
                        transition.Effects,
                        writer,
                        ref connection);
                    if (effectError is not null)
                    {
                        return effectError;
                    }
                }
                finally
                {
                    if (copiedStableFrame)
                    {
                        _configuration.StableFrameCopied?.Invoke();
                    }
                }

                await ApplyLoopDirectiveAsync(
                    transition.LoopDirective,
                    connection,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            completionException = exception;
            throw;
        }
        finally
        {
            CloseConnection(ref connection);
            _connectionState.PublishUnavailable();
            writer.TryComplete(completionException);
        }
    }

    private IracingTelemetryLifecycleInput ObserveLifecycleInput(
        IracingTelemetryLifecycleState state,
        ref IracingSharedMemoryConnection? connection,
        out bool copiedStableFrame)
    {
        copiedStableFrame = false;
        if (IracingTelemetryLifecycleReducer.RequiresReaderOpen(state))
        {
            if (connection is not null)
            {
                throw new InvalidOperationException(
                    "A lifecycle awaiting a reader cannot retain an open reader.");
            }

            if (!IracingSharedMemoryConnection.TryOpen(
                    _configuration.MemoryMapName,
                    _configuration.DataValidEventName,
                    _configuration.TimeProvider,
                    out connection))
            {
                return new IracingTelemetryLifecycleInput.ReaderOpenFailed(
                    MeasureLogicalConnectionAge(state));
            }

            var identity =
                IracingTelemetryLifecycleReducer.TryGetRetainedConnectionIdentity(
                    state,
                    out var retainedIdentity)
                    ? retainedIdentity!
                    : IracingConnectionIdentity.Create(
                        $"connection:{Guid.CreateVersion7():D}");
            Volatile.Write(ref _currentConnection, connection);
            return new IracingTelemetryLifecycleInput.ReaderOpened(identity);
        }

        if (connection is null)
        {
            throw new InvalidOperationException(
                "A lifecycle ready to read must own an open reader.");
        }

        IracingReadResult readResult;
        try
        {
            readResult = connection.TryRead();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            readResult = IracingReadResult.Disconnected.Instance;
        }

        switch (readResult)
        {
            case IracingReadResult.Snapshot snapshot:
            {
                copiedStableFrame = true;
                var frame = snapshot.Frame;
                var identity = IracingTelemetryLifecycleReducer
                    .GetConnectionIdentityForRead(state);
                var sample = IracingFrameDecoder.Decode(
                    frame,
                    identity.Value,
                    _configuration.TimeProvider);
                var timestamp = IracingMonotonicTimestamp.Capture(
                    _configuration.TimeProvider);
                var previousSampleAge = MeasureLogicalConnectionAge(state, timestamp);
                return sample.IsSuccess
                    ? new IracingTelemetryLifecycleInput.SampleAccepted(
                        frame,
                        sample.Value,
                        timestamp,
                        previousSampleAge)
                    : new IracingTelemetryLifecycleInput.SampleRejected(
                        sample.Error!,
                        previousSampleAge);
            }
            case IracingReadResult.NoData:
                return new IracingTelemetryLifecycleInput.NoData(
                    MeasureLogicalConnectionAge(state));
            case IracingReadResult.Invalid:
                return new IracingTelemetryLifecycleInput.InvalidFrame(
                    MeasureLogicalConnectionAge(state));
            case IracingReadResult.Disconnected:
                return IracingTelemetryLifecycleInput.SdkDisconnected.Instance;
            default:
                throw new InvalidOperationException("The iRacing read status is undefined.");
        }
    }

    private IracingLogicalConnectionAge MeasureLogicalConnectionAge(
        IracingTelemetryLifecycleState state) => MeasureLogicalConnectionAge(
            state,
            IracingMonotonicTimestamp.Capture(_configuration.TimeProvider));

    private IracingLogicalConnectionAge MeasureLogicalConnectionAge(
        IracingTelemetryLifecycleState state,
        IracingMonotonicTimestamp timestamp) =>
        IracingTelemetryLifecycleReducer.TryGetLastSuccessfullyDecodedSampleTimestamp(
            state,
            out var lastSuccessfullyDecodedSampleTimestamp)
            ? new IracingLogicalConnectionAge.Measured(
                lastSuccessfullyDecodedSampleTimestamp!.ElapsedUntil(
                    timestamp,
                    _configuration.TimeProvider))
            : IracingLogicalConnectionAge.NotEstablished.Instance;

    private Error? ApplyLifecycleEffects(
        IReadOnlyList<IracingTelemetryLifecycleEffect> effects,
        ChannelWriter<TelemetryEvent> writer,
        ref IracingSharedMemoryConnection? connection)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case IracingTelemetryLifecycleEffect.CloseReader:
                    if (connection is null)
                    {
                        throw new InvalidOperationException(
                            "The telemetry reducer cannot close a missing reader.");
                    }

                    CloseConnection(ref connection);
                    break;
                case IracingTelemetryLifecycleEffect.PublishReplayUnavailable:
                    _connectionState.PublishUnavailable();
                    break;
                case IracingTelemetryLifecycleEffect.PublishReplayFrame replay:
                    _connectionState.PublishAvailable(replay.Frame, replay.Sample);
                    break;
                case IracingTelemetryLifecycleEffect.PublishTelemetry telemetry:
                    if (!writer.TryWrite(telemetry.Event))
                    {
                        return IracingErrors.TelemetryBufferOverflow;
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        "The telemetry lifecycle effect is undefined.");
            }
        }

        return null;
    }

    private async ValueTask ApplyLoopDirectiveAsync(
        IracingTelemetryLoopDirective directive,
        IracingSharedMemoryConnection? connection,
        CancellationToken cancellationToken)
    {
        switch (directive)
        {
            case IracingTelemetryLoopDirective.Continue:
                if (connection is null)
                {
                    throw new InvalidOperationException(
                        "Continuing telemetry requires an open reader.");
                }

                return;
            case IracingTelemetryLoopDirective.WaitForData:
                if (connection is null)
                {
                    throw new InvalidOperationException(
                        "Waiting for telemetry data requires an open reader.");
                }

                await connection.WaitForDataAsync(
                    _configuration.Options.DataWaitTimeout,
                    cancellationToken).ConfigureAwait(false);
                return;
            case IracingTelemetryLoopDirective.DelayBeforeReconnect:
                if (connection is not null)
                {
                    throw new InvalidOperationException(
                        "Reconnect delay requires a closed reader.");
                }

                await Task.Delay(
                    _configuration.Options.ReconnectInterval,
                    cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException(
                    "The telemetry loop directive is undefined.");
        }
    }

    private void CloseConnection(ref IracingSharedMemoryConnection? connection)
    {
        var toDispose = connection;
        connection = null;
        if (toDispose is null)
        {
            return;
        }

        _ = Interlocked.CompareExchange(ref _currentConnection, null, toDispose);
        toDispose.Dispose();
    }

    private async ValueTask<bool> WaitToReadAsync(
        ChannelReader<TelemetryEvent> reader,
        CancellationToken linkedCancellationToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(linkedCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _disposeSource.IsCancellationRequested &&
            !callerCancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async ValueTask AwaitProducerShutdownAsync(
        Task<Error?> producer,
        Task<Exception?> producerStopped,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Consumer cancellation owns the expected producer shutdown.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Adapter disposal may close the native wait while producer shutdown is in flight.
        }

        var producerFault = await producerStopped.ConfigureAwait(false);
        if (producerFault is not null)
        {
            ExceptionDispatchInfo.Capture(producerFault).Throw();
        }
    }

    private abstract record ObservationAdmission
    {
        private ObservationAdmission()
        {
        }

        internal sealed record Disposed : ObservationAdmission
        {
            public static Disposed Instance { get; } = new();

            private Disposed()
            {
            }
        }

        internal sealed record AlreadyObserving : ObservationAdmission
        {
            public static AlreadyObserving Instance { get; } = new();

            private AlreadyObserving()
            {
            }
        }

        internal sealed record Started(TaskCompletionSource<Exception?> ProducerStopped) :
            ObservationAdmission;
    }

}
