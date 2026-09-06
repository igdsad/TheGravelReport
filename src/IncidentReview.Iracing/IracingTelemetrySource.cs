using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IncidentReview.Domain;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Results;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing;

internal sealed class IracingTelemetrySource : ITelemetrySource, IAsyncDisposable
{
    private readonly IracingProtocolConfiguration _configuration;
    private readonly IracingConnectionState _connectionState;
    private readonly CancellationTokenSource _disposeSource = new();
    private IracingSharedMemoryConnection? _currentConnection;
    private int _isDisposed;
    private int _isObserving;

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
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            yield break;
        }

        if (Interlocked.CompareExchange(ref _isObserving, 1, 0) != 0)
        {
            yield return TelemetryUnavailable.Create(IracingErrors.TelemetryAlreadyObserved);
            yield break;
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
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
        var producer = ProduceAsync(channel.Writer, linkedSource.Token);

        try
        {
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
                if (terminalError is not null)
                {
                    yield return TelemetryUnavailable.Create(terminalError);
                }
            }
        }
        finally
        {
            linkedSource.Cancel();
            await AwaitProducerShutdownAsync(producer, linkedSource.Token).ConfigureAwait(false);
            _connectionState.SetUnavailable();
            Interlocked.Exchange(ref _isObserving, 0);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _connectionState.SetUnavailable();
            _disposeSource.Cancel();
            Interlocked.Exchange(ref _currentConnection, null)?.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<Error?> ProduceAsync(
        ChannelWriter<TelemetryEvent> writer,
        CancellationToken cancellationToken)
    {
        Exception? completionException = null;
        IracingSharedMemoryConnection? connection = null;
        string? connectionIdentity = null;
        var announcedConnection = false;
        var reportedUnavailable = false;
        TelemetryTransitionState? lastTransition = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (connection is null)
                {
                    if (!IracingSharedMemoryConnection.TryOpen(
                            _configuration.MemoryMapName,
                            _configuration.DataValidEventName,
                            _configuration.TimeProvider,
                            out connection))
                    {
                        if (!reportedUnavailable)
                        {
                            reportedUnavailable = true;
                            if (!writer.TryWrite(TelemetryUnavailable.Create(
                                    IracingErrors.TelemetryUnavailable)))
                            {
                                return IracingErrors.TelemetryBufferOverflow;
                            }
                        }

                        await Task.Delay(
                            _configuration.Options.ReconnectInterval,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    reportedUnavailable = false;
                    connectionIdentity = $"connection:{Guid.CreateVersion7():D}";
                    Volatile.Write(ref _currentConnection, connection);
                }

                IracingReadResult readResult;
                try
                {
                    readResult = connection!.TryRead();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    readResult = IracingReadResult.Disconnected;
                }

                switch (readResult.Status)
                {
                    case IracingReadStatus.Snapshot:
                    {
                        try
                        {
                            _connectionState.ObserveStableFrame(readResult.Snapshot!);
                            var sample = IracingFrameDecoder.Decode(
                                readResult.Snapshot!,
                                connectionIdentity!,
                                _configuration.TimeProvider);
                            if (!sample.IsSuccess)
                            {
                                CloseConnection(ref connection);
                                connectionIdentity = null;
                                lastTransition = null;
                                if (announcedConnection)
                                {
                                    announcedConnection = false;
                                    if (!writer.TryWrite(TelemetryDisconnected.Instance))
                                    {
                                        return IracingErrors.TelemetryBufferOverflow;
                                    }
                                }

                                if (!writer.TryWrite(TelemetryUnavailable.Create(sample.Error!)))
                                {
                                    return IracingErrors.TelemetryBufferOverflow;
                                }

                                break;
                            }

                            if (!announcedConnection)
                            {
                                _connectionState.SetAvailable();
                                if (!writer.TryWrite(TelemetryConnected.Instance))
                                {
                                    return IracingErrors.TelemetryBufferOverflow;
                                }

                                announcedConnection = true;
                            }

                            var transition = TelemetryTransitionState.From(sample.Value);
                            if (transition != lastTransition)
                            {
                                if (!writer.TryWrite(TelemetrySampleObserved.Create(sample.Value)))
                                {
                                    return IracingErrors.TelemetryBufferOverflow;
                                }

                                lastTransition = transition;
                            }
                        }
                        finally
                        {
                            _configuration.StableFrameCopied?.Invoke();
                        }

                        break;
                    }
                    case IracingReadStatus.NoData:
                        await connection!.WaitForDataAsync(
                            _configuration.Options.DataWaitTimeout,
                            cancellationToken).ConfigureAwait(false);
                        break;
                    case IracingReadStatus.Invalid:
                        CloseConnection(ref connection);
                        connectionIdentity = null;
                        lastTransition = null;
                        if (announcedConnection)
                        {
                            announcedConnection = false;
                            if (!writer.TryWrite(TelemetryDisconnected.Instance))
                            {
                                return IracingErrors.TelemetryBufferOverflow;
                            }
                        }

                        if (!writer.TryWrite(TelemetryUnavailable.Create(
                                IracingErrors.InvalidTelemetryFrame)))
                        {
                            return IracingErrors.TelemetryBufferOverflow;
                        }

                        break;
                    case IracingReadStatus.Disconnected:
                        CloseConnection(ref connection);
                        connectionIdentity = null;
                        lastTransition = null;
                        if (announcedConnection)
                        {
                            announcedConnection = false;
                            if (!writer.TryWrite(TelemetryDisconnected.Instance))
                            {
                                return IracingErrors.TelemetryBufferOverflow;
                            }
                        }

                        break;
                    default:
                        throw new InvalidOperationException("The iRacing read status is undefined.");
                }

                if (connection is null)
                {
                    await Task.Delay(
                        _configuration.Options.ReconnectInterval,
                        cancellationToken).ConfigureAwait(false);
                }
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
            _connectionState.SetUnavailable();
            writer.TryComplete(completionException);
        }
    }

    private void CloseConnection(ref IracingSharedMemoryConnection? connection)
    {
        _connectionState.SetUnavailable();
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
    }

    private sealed record TelemetryTransitionState(
        SimulatorSessionDescriptor Session,
        IncidentCounter IncidentCounter,
        OnTrackState OnTrackState)
    {
        public static TelemetryTransitionState From(TelemetrySample sample) => new(
            sample.Session,
            sample.IncidentCounter,
            sample.OnTrackState);
    }
}
