using System.Collections.ObjectModel;
using IncidentReview.Iracing.Options;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Iracing.Replay;
using IncidentReview.Replay.Contracts;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing.Testing;

/// <summary>Creates explicit test seams without exposing SDK protocol types to production callers.</summary>
public static class IracingTestingRegistration
{
    /// <summary>Creates a real shared-memory telemetry reader over isolated test object names.</summary>
    public static ITelemetrySource CreateTelemetrySource(
        string memoryMapName,
        string dataValidEventName,
        TimeProvider? timeProvider = null,
        IracingOptions? options = null,
        Action? stableFrameCopied = null)
    {
        ValidateObjectName(memoryMapName, nameof(memoryMapName));
        ValidateObjectName(dataValidEventName, nameof(dataValidEventName));
        return new IracingTelemetrySource(
            new IracingProtocolConfiguration(
                memoryMapName,
                dataValidEventName,
                options ?? IracingOptions.Default,
                timeProvider ?? TimeProvider.System,
                stableFrameCopied),
            new IracingConnectionState());
    }

    /// <summary>Creates a replay controller with a deterministic command recorder.</summary>
    public static IracingTestReplayContext CreateReplayContext(
        IracingTestDeliveryMode deliveryMode = IracingTestDeliveryMode.Delivered,
        bool telemetryAvailable = true)
    {
        if (!Enum.IsDefined(deliveryMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveryMode),
                deliveryMode,
                "The test delivery mode is undefined.");
        }

        var sender = new RecordingReplayMessageSender(deliveryMode);
        return new IracingTestReplayContext(
            new IracingReplayController(
                sender,
                new IracingConnectionState(telemetryAvailable)),
            sender);
    }

    /// <summary>Opens a focused probe over the production torn-copy implementation.</summary>
    public static IracingTestFrameProbe CreateFrameProbe(
        string memoryMapName,
        string dataValidEventName,
        TimeProvider? timeProvider = null,
        Action? betweenSessionInformationCopies = null)
    {
        ValidateObjectName(memoryMapName, nameof(memoryMapName));
        ValidateObjectName(dataValidEventName, nameof(dataValidEventName));
        return IracingSharedMemoryConnection.TryOpen(
            memoryMapName,
            dataValidEventName,
            timeProvider ?? TimeProvider.System,
            out var connection,
            betweenSessionInformationCopies)
            ? new IracingTestFrameProbe(connection!)
            : throw new InvalidOperationException("The test telemetry endpoint is unavailable.");
    }

    /// <summary>Decodes the durable iRacing SubSessionID evidence for focused parser tests.</summary>
    public static long? ReadSubSessionId(string sessionInfo)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);
        return IracingSessionInfoDecoder.TryGetSubSessionId(sessionInfo);
    }

    /// <summary>
    /// Verifies that the production wait-only boundary can open and release a named data event.
    /// </summary>
    public static bool CanOpenDataValidEvent(string dataValidEventName)
    {
        ValidateObjectName(dataValidEventName, nameof(dataValidEventName));
        if (!WindowsDataValidEvent.TryOpen(dataValidEventName, out var dataEvent))
        {
            return false;
        }

        dataEvent.Dispose();
        return true;
    }

    private static void ValidateObjectName(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);
        if (name.Length is 0 or > 260 ||
            string.IsNullOrWhiteSpace(name) ||
            name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A test kernel-object name must be non-blank, bounded text without controls.",
                parameterName);
        }
    }
}

/// <summary>Controls the deterministic replay-delivery outcome exposed to tests.</summary>
public enum IracingTestDeliveryMode
{
    /// <summary>Records and accepts the command.</summary>
    Delivered = 0,

    /// <summary>Reports that the registered-message endpoint is unavailable.</summary>
    EndpointUnavailable = 1,

    /// <summary>Reports that Windows rejected the notification request.</summary>
    DeliveryRejected = 2,
}

/// <summary>Contains a replay controller and its independent wire-command record.</summary>
public sealed class IracingTestReplayContext
{
    private readonly RecordingReplayMessageSender _sender;

    internal IracingTestReplayContext(
        IReplayController controller,
        RecordingReplayMessageSender sender)
    {
        Controller = controller;
        _sender = sender;
    }

    /// <summary>Gets the replay contract under test.</summary>
    public IReplayController Controller { get; }

    /// <summary>Gets a stable snapshot of all delivered wire commands.</summary>
    public IReadOnlyList<IracingTestReplayMessage> Messages =>
        new ReadOnlyCollection<IracingTestReplayMessage>(_sender.Snapshot());
}

/// <summary>Represents independently inspectable 32-bit iRacing broadcast payloads.</summary>
public sealed record IracingTestReplayMessage(int Command, int WParam, int LParam);

/// <summary>Classifies one focused shared-memory copy attempt.</summary>
public enum IracingTestFrameOutcome
{
    /// <summary>No new stable frame was available.</summary>
    NoData,

    /// <summary>The shared-memory header reported a disconnected simulator.</summary>
    Disconnected,

    /// <summary>A stable, bounded snapshot was copied.</summary>
    Snapshot,

    /// <summary>The shared-memory structure was malformed.</summary>
    Invalid,
}

/// <summary>Provides deterministic access to the production frame-copy boundary.</summary>
public sealed class IracingTestFrameProbe : IDisposable
{
    private readonly IracingSharedMemoryConnection _connection;
    private int _isDisposed;

    internal IracingTestFrameProbe(IracingSharedMemoryConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Gets the number of stable variable-header decodes performed by this connection.</summary>
    public int VariableMetadataDecodeCount => _connection.VariableMetadataDecodeCount;

    /// <summary>Gets the number of stable session-information decodes performed by this connection.</summary>
    public int SessionInformationDecodeCount => _connection.SessionInformationDecodeCount;

    /// <summary>Attempts one bounded read without waiting for another frame.</summary>
    public IracingTestFrameOutcome Read()
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        return _connection.TryRead().Status switch
        {
            IracingReadStatus.NoData => IracingTestFrameOutcome.NoData,
            IracingReadStatus.Disconnected => IracingTestFrameOutcome.Disconnected,
            IracingReadStatus.Snapshot => IracingTestFrameOutcome.Snapshot,
            IracingReadStatus.Invalid => IracingTestFrameOutcome.Invalid,
            _ => throw new InvalidOperationException("The iRacing read status is undefined."),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class RecordingReplayMessageSender : IReplayMessageSender
{
    private readonly Lock _lock = new();
    private readonly IracingTestDeliveryMode _deliveryMode;
    private readonly List<IracingTestReplayMessage> _messages = [];

    public RecordingReplayMessageSender(IracingTestDeliveryMode deliveryMode)
    {
        _deliveryMode = deliveryMode;
    }

    public ReplaySendOutcome Send(ReplayBroadcastCommand command)
    {
        lock (_lock)
        {
            _messages.Add(new IracingTestReplayMessage(
                (int)command.Message,
                command.WParam,
                command.LParam));
        }

        return _deliveryMode switch
        {
            IracingTestDeliveryMode.Delivered => ReplaySendOutcome.Delivered,
            IracingTestDeliveryMode.EndpointUnavailable => ReplaySendOutcome.EndpointUnavailable,
            IracingTestDeliveryMode.DeliveryRejected => ReplaySendOutcome.DeliveryRejected,
            _ => throw new InvalidOperationException("The test delivery mode is undefined."),
        };
    }

    public IracingTestReplayMessage[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _messages];
        }
    }
}
