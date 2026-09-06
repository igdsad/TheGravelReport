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
        bool telemetryAvailable = true,
        bool autoApplyCommands = true,
        TimeSpan? confirmationTimeout = null,
        string? sessionInfo = null)
    {
        if (!Enum.IsDefined(deliveryMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveryMode),
                deliveryMode,
                "The test delivery mode is undefined.");
        }

        var connectionState = new IracingConnectionState();
        var initialFrame = CreateTestFrame(
            new IracingTestReplayState(
                ReplaySessionNumber: 0,
                ReplaySessionTimeMilliseconds: 0,
                ReplayPlaySpeed: 1,
                ReplayPlaySlowMotion: false,
                CameraState: 1,
                CameraCarIndex: 4,
                CameraGroupNumber: 1,
                CameraNumber: 10,
                SessionInfo: sessionInfo ?? DefaultReplaySessionInfo));
        if (telemetryAvailable)
        {
            connectionState.PublishAvailable(initialFrame);
        }

        var sender = new RecordingReplayMessageSender(
            deliveryMode,
            autoApplyCommands
                ? command => ApplyTestCommand(connectionState, command)
                : null);
        return new IracingTestReplayContext(
            new IracingReplayController(
                sender,
                connectionState,
                confirmationTimeout),
            new IracingReplayContextReader(connectionState),
            sender,
            connectionState);
    }

    /// <summary>
    /// Creates telemetry and replay adapters over the same deterministic connection state.
    /// </summary>
    public static IracingTestIntegrationContext CreateIntegrationContext(
        string memoryMapName,
        string dataValidEventName,
        IracingOptions? options = null,
        TimeSpan? confirmationTimeout = null) => CreateIntegrationContext(
            memoryMapName,
            dataValidEventName,
            options,
            confirmationTimeout,
            TimeProvider.System,
            stableFrameCopied: null);

    /// <summary>
    /// Creates telemetry and replay adapters with deterministic time and frame-copy seams.
    /// </summary>
    public static IracingTestIntegrationContext CreateIntegrationContext(
        string memoryMapName,
        string dataValidEventName,
        IracingOptions? options,
        TimeSpan? confirmationTimeout,
        TimeProvider? timeProvider,
        Action? stableFrameCopied)
    {
        ValidateObjectName(memoryMapName, nameof(memoryMapName));
        ValidateObjectName(dataValidEventName, nameof(dataValidEventName));
        var connectionState = new IracingConnectionState();
        var sender = new RecordingReplayMessageSender(IracingTestDeliveryMode.Delivered);
        var source = new IracingTelemetrySource(
            new IracingProtocolConfiguration(
                memoryMapName,
                dataValidEventName,
                options ?? IracingOptions.Default,
                timeProvider ?? TimeProvider.System,
                stableFrameCopied),
            connectionState);
        return new IracingTestIntegrationContext(
            source,
            new IracingReplayController(sender, connectionState, confirmationTimeout),
            new IracingReplayContextReader(connectionState),
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

    /// <summary>Classifies replay mode from the official playback and camera-state fields.</summary>
    public static bool IsReplayMode(bool isReplayPlaying, int cameraState) =>
        IracingFrameDecoder.IsReplayMode(isReplayPlaying, cameraState);

    /// <summary>Parses the replay-focus subset of official iRacing session YAML.</summary>
    public static IracingTestReplayMetadata? ReadReplayMetadata(string sessionInfo)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);
        if (!IracingSessionInfoDecoder.TryGetReplayMetadata(sessionInfo, out var metadata))
        {
            return null;
        }

        return new IracingTestReplayMetadata(
            metadata!.PlayerCarIndex,
            metadata.PlayerCarNumberRaw,
            metadata.CameraGroups.Select(static group =>
                new IracingTestCameraGroup(
                    group.Number,
                    group.Name,
                    [.. group.CameraNumbers])).ToArray());
    }

    /// <summary>Sends one raw replay message through the production Windows sender.</summary>
    public static IracingTestDeliveryMode SendWindowsReplayMessage(
        IracingTestReplayMessage message,
        string registeredMessageName)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredMessageName);
        if (!Enum.IsDefined(typeof(IracingBroadcastMessage), message.Command))
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                message.Command,
                "The replay broadcast command is undefined.");
        }

        var outcome = new WindowsReplayMessageSender(registeredMessageName).Send(
            new ReplayBroadcastCommand(
            (IracingBroadcastMessage)message.Command,
            message.WParam,
            message.LParam));
        return outcome switch
        {
            ReplaySendOutcome.Delivered => IracingTestDeliveryMode.Delivered,
            ReplaySendOutcome.EndpointUnavailable => IracingTestDeliveryMode.EndpointUnavailable,
            ReplaySendOutcome.DeliveryRejected => IracingTestDeliveryMode.DeliveryRejected,
            _ => throw new InvalidOperationException("The replay delivery outcome is undefined."),
        };
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

    private static ReplayFrameState CreateTestFrame(IracingTestReplayState state)
    {
        var metadata = IracingSessionInfoDecoder.ReadReplayContextMetadata(
            state.SessionInfo);

        return new ReplayFrameState(
            state.ReplaySessionNumber,
            state.ReplaySessionTimeMilliseconds,
            state.ReplayPlaySpeed,
            state.ReplayPlaySlowMotion,
            state.CameraState,
            state.CameraCarIndex,
            state.CameraGroupNumber,
            state.CameraNumber,
            metadata);
    }

    private static void ApplyTestCommand(
        IracingConnectionState state,
        ReplayBroadcastCommand command)
    {
        var observation = state.Read();
        if (observation is not ReplayFrameObservation.Available available)
        {
            return;
        }

        var current = available.Frame;
        var next = command.Message switch
        {
            IracingBroadcastMessage.ReplaySearchSessionTime => current with
            {
                ReplaySessionNumber = unchecked((short)((uint)command.WParam >> 16)),
                ReplaySessionTimeMilliseconds = command.LParam,
            },
            IracingBroadcastMessage.ReplaySetPlaySpeed => current with
            {
                ReplayPlaySpeed = unchecked((short)((uint)command.WParam >> 16)),
                ReplayPlaySlowMotion = unchecked((short)(uint)command.LParam) != 0,
            },
            IracingBroadcastMessage.CameraSwitchNumber => ApplyTestCameraCommand(
                current,
                command),
            _ => current,
        };
        state.PublishAvailable(next);
    }

    private static ReplayFrameState ApplyTestCameraCommand(
        ReplayFrameState current,
        ReplayBroadcastCommand command)
    {
        var rawCarNumber = unchecked((short)((uint)command.WParam >> 16));
        var carIndex = current.Metadata.Player?.CarNumberRaw == rawCarNumber
            ? current.Metadata.Player.CarIndex
            : current.CameraCarIndex;
        return current with
        {
            CameraCarIndex = carIndex,
            CameraGroupNumber = unchecked((short)(uint)command.LParam),
            CameraNumber = unchecked((short)((uint)command.LParam >> 16)),
        };
    }

    private const string DefaultReplaySessionInfo = """
        DriverInfo:
         DriverCarIdx: 4
         Drivers:
         - CarIdx: 4
           UserName: René Test Driver
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 1
           GroupName: Cockpit
           Cameras:
           - CameraNum: 10
         - GroupNum: 2
           GroupName: TV1
           Cameras:
           - CameraNum: 20
           - CameraNum: 21
        """;
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
    private readonly IracingConnectionState _connectionState;

    internal IracingTestReplayContext(
        IReplayController controller,
        IReplayContextReader contextReader,
        RecordingReplayMessageSender sender,
        IracingConnectionState connectionState)
    {
        Controller = controller;
        ContextReader = contextReader;
        _sender = sender;
        _connectionState = connectionState;
    }

    /// <summary>Gets the replay contract under test.</summary>
    public IReplayController Controller { get; }

    /// <summary>Gets the read-only replay-context contract under test.</summary>
    public IReplayContextReader ContextReader { get; }

    /// <summary>Gets a stable snapshot of all delivered wire commands.</summary>
    public IReadOnlyList<IracingTestReplayMessage> Messages =>
        new ReadOnlyCollection<IracingTestReplayMessage>(_sender.Snapshot());

    /// <summary>Publishes a later deterministic SDK frame for confirmation tests.</summary>
    public void ObserveFrame(IracingTestReplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _connectionState.PublishAvailable(CreateFrame(state));
    }

    /// <summary>Marks the deterministic SDK connection unavailable.</summary>
    public void Disconnect() => _connectionState.PublishUnavailable();

    private static ReplayFrameState CreateFrame(IracingTestReplayState state)
    {
        var metadata = IracingSessionInfoDecoder.ReadReplayContextMetadata(
            state.SessionInfo);
        return new ReplayFrameState(
            state.ReplaySessionNumber,
            state.ReplaySessionTimeMilliseconds,
            state.ReplayPlaySpeed,
            state.ReplayPlaySlowMotion,
            state.CameraState,
            state.CameraCarIndex,
            state.CameraGroupNumber,
            state.CameraNumber,
            metadata);
    }
}

/// <summary>Pairs production telemetry reads with deterministic replay-message delivery.</summary>
public sealed class IracingTestIntegrationContext
{
    private readonly RecordingReplayMessageSender _sender;

    internal IracingTestIntegrationContext(
        ITelemetrySource telemetry,
        IReplayController controller,
        IReplayContextReader contextReader,
        RecordingReplayMessageSender sender)
    {
        Telemetry = telemetry;
        Controller = controller;
        ContextReader = contextReader;
        _sender = sender;
    }

    /// <summary>Gets the shared-memory telemetry adapter.</summary>
    public ITelemetrySource Telemetry { get; }

    /// <summary>Gets the replay controller observing every copied telemetry frame.</summary>
    public IReplayController Controller { get; }

    /// <summary>Gets the read-only context reader sharing the telemetry connection state.</summary>
    public IReplayContextReader ContextReader { get; }

    /// <summary>Gets a stable snapshot of delivered commands.</summary>
    public IReadOnlyList<IracingTestReplayMessage> Messages =>
        new ReadOnlyCollection<IracingTestReplayMessage>(_sender.Snapshot());
}

/// <summary>Represents independently inspectable 32-bit iRacing broadcast payloads.</summary>
public sealed record IracingTestReplayMessage(int Command, int WParam, int LParam);

/// <summary>Represents the confirmation fields copied from one stable SDK frame.</summary>
public sealed record IracingTestReplayState(
    int? ReplaySessionNumber,
    long? ReplaySessionTimeMilliseconds,
    int? ReplayPlaySpeed,
    bool? ReplayPlaySlowMotion,
    int? CameraState,
    int? CameraCarIndex,
    int? CameraGroupNumber,
    int? CameraNumber,
    string SessionInfo);

/// <summary>Represents replay-focus metadata decoded for integration tests.</summary>
public sealed record IracingTestReplayMetadata(
    int PlayerCarIndex,
    int PlayerCarNumberRaw,
    IReadOnlyList<IracingTestCameraGroup> CameraGroups);

/// <summary>Represents one decoded camera group and its ordered camera numbers.</summary>
public sealed record IracingTestCameraGroup(
    int Number,
    string Name,
    IReadOnlyList<int> CameraNumbers);

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
        return _connection.TryRead() switch
        {
            IracingReadResult.NoData => IracingTestFrameOutcome.NoData,
            IracingReadResult.Disconnected => IracingTestFrameOutcome.Disconnected,
            IracingReadResult.Snapshot => IracingTestFrameOutcome.Snapshot,
            IracingReadResult.Invalid => IracingTestFrameOutcome.Invalid,
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
    private readonly Action<ReplayBroadcastCommand>? _onDelivered;
    private readonly List<IracingTestReplayMessage> _messages = [];

    public RecordingReplayMessageSender(
        IracingTestDeliveryMode deliveryMode,
        Action<ReplayBroadcastCommand>? onDelivered = null)
    {
        _deliveryMode = deliveryMode;
        _onDelivered = onDelivered;
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

        if (_deliveryMode == IracingTestDeliveryMode.Delivered)
        {
            _onDelivered?.Invoke(command);
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
