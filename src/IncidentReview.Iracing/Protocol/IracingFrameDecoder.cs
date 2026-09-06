using System.Buffers.Binary;
using System.Globalization;
using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing.Protocol;

internal static class IracingFrameDecoder
{
    private static readonly SimulatorCode Simulator =
        SimulatorCode.TryCreate("iracing").Value;

    public static Result<TelemetrySample> Decode(
        IracingFrameSnapshot snapshot,
        string connectionIdentity,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionIdentity);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!TryReadBoolean(snapshot, IracingProtocol.IsReplayPlayingVariable, out var isReplayPlaying) ||
            !TryReadBoolean(snapshot, IracingProtocol.IsOnTrackVariable, out var isOnTrack))
        {
            return Invalid();
        }

        var isSessionScreen = false;
        if (snapshot.Variables.ContainsKey(IracingProtocol.CameraStateVariable))
        {
            if (!TryReadBitField(
                    snapshot,
                    IracingProtocol.CameraStateVariable,
                    out var cameraState))
            {
                return Invalid();
            }

            isSessionScreen = (cameraState & (int)IracingCameraState.IsSessionScreen) != 0;
        }

        var isReplay = IsReplayMode(isReplayPlaying, cameraState: isSessionScreen ? 1 : 0);

        var sessionNumberVariable = isReplay
            ? IracingProtocol.ReplaySessionNumberVariable
            : IracingProtocol.SessionNumberVariable;
        var sessionTimeVariable = isReplay
            ? IracingProtocol.ReplaySessionTimeVariable
            : IracingProtocol.SessionTimeVariable;
        if (!TryReadInt32(snapshot, sessionNumberVariable, out var sessionNumberValue) ||
            !TryReadDouble(snapshot, sessionTimeVariable, out var sessionSeconds) ||
            !TryConvertSessionMilliseconds(sessionSeconds, out var sessionMilliseconds))
        {
            return Invalid();
        }

        var sessionNumber = SessionNumber.TryCreate(sessionNumberValue);
        var sessionTime = SessionTime.TryCreateMilliseconds(sessionMilliseconds);
        var observedAt = UtcInstant.TryCreateUnixMilliseconds(
            timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        if (!sessionNumber.IsSuccess || !sessionTime.IsSuccess ||
            !observedAt.IsSuccess)
        {
            return Invalid();
        }

        var subSessionId = snapshot.SubSessionId;
        var isDurable = subSessionId is not null;
        var sessionKeyText = isDurable
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"v1:subsession:{subSessionId!.Value}:session:{sessionNumber.Value.Value}")
            : connectionIdentity;
        var sessionKey = SimulatorSessionKey.TryCreate(sessionKeyText);
        var descriptor = SimulatorSessionDescriptor.TryCreate(
            Simulator,
            sessionKey.IsSuccess ? sessionKey.Value : null,
            sessionNumber.Value,
            isReplay ? SessionMode.Replay : SessionMode.Live,
            isDurable
                ? SimulatorIdentityScope.Durable
                : SimulatorIdentityScope.ConnectionScoped);
        var position = ReplayPosition.TryCreate(sessionNumber.Value, sessionTime.Value);
        if (!sessionKey.IsSuccess || !descriptor.IsSuccess || !position.IsSuccess)
        {
            return Invalid();
        }

        if (!TryReadOptionalLap(snapshot, out var lap) ||
            !TryReadOptionalLapDistance(snapshot, out var lapDistance))
        {
            return Invalid();
        }

        var incidentMetadata = IracingSessionInfoDecoder.ReadIncidentMetadata(
            snapshot.SessionInfo);
        var hasLiveSessionNumber = TryReadInt32(
            snapshot,
            IracingProtocol.SessionNumberVariable,
            out var liveSessionNumber);
        var rosterMatchesFrame = incidentMetadata.CurrentSessionNumber is not null &&
            hasLiveSessionNumber &&
            incidentMetadata.CurrentSessionNumber.Value == liveSessionNumber;
        if (!TryCreateIncidentCounters(
                snapshot,
                incidentMetadata,
                rosterMatchesFrame,
                lap,
                lapDistance,
                out var incidentCounters))
        {
            return Invalid();
        }

        var sample = TelemetrySample.TryCreate(
            descriptor.Value,
            position.Value,
            incidentCounters,
            isOnTrack ? OnTrackState.OnTrack : OnTrackState.NotOnTrack,
            observedAt.Value);
        return sample.IsSuccess ? sample : Invalid();
    }

    private static bool TryCreateIncidentCounters(
        IracingFrameSnapshot snapshot,
        IracingIncidentMetadata metadata,
        bool rosterMatchesFrame,
        LapNumber? localLap,
        LapDistance? localLapDistance,
        out IReadOnlyList<ParticipantIncidentCounter> counters)
    {
        if (!rosterMatchesFrame)
        {
            counters = [];
            return true;
        }

        var result = new List<ParticipantIncidentCounter>();
        foreach (var source in metadata.Participants)
        {
            var isLocal = metadata.PlayerCarIndex == source.CarIndex;
            var rawIncidentCount = source.IncidentCount;
            if (rawIncidentCount is null &&
                isLocal &&
                TryReadInt32(
                    snapshot,
                    IracingProtocol.TeamIncidentCountVariable,
                    out var localTeamIncidentCount))
            {
                rawIncidentCount = localTeamIncidentCount;
            }

            if (rawIncidentCount is not { } incidentCountValue)
            {
                continue;
            }

            var identity = ParticipantIdentity.TryCreate(source.Identity);
            var participant = identity.IsSuccess
                ? TryCreateParticipantWithSafeDisplayMetadata(
                    identity.Value,
                    source.DriverName,
                    source.TeamName,
                    source.CarNumber)
                : null;
            var incidentCounter = IncidentCounter.TryCreate(incidentCountValue);
            var counter = ParticipantIncidentCounter.TryCreate(
                participant,
                incidentCounter.IsSuccess ? incidentCounter.Value : null,
                isLocal ? localLap : null,
                isLocal ? localLapDistance : null);
            if (counter.IsSuccess)
            {
                result.Add(counter.Value);
            }
        }

        counters = result.AsReadOnly();
        return true;
    }

    private static IncidentParticipant? TryCreateParticipantWithSafeDisplayMetadata(
        ParticipantIdentity identity,
        string? driverName,
        string? teamName,
        string? carNumber)
    {
        var driverProbe = IncidentParticipant.TryCreate(
            identity,
            driverName,
            teamName: null,
            carNumber: null);
        var teamProbe = IncidentParticipant.TryCreate(
            identity,
            driverName: null,
            teamName,
            carNumber: null);
        var numberProbe = IncidentParticipant.TryCreate(
            identity,
            driverName: null,
            teamName: null,
            carNumber);
        var result = IncidentParticipant.TryCreate(
            identity,
            driverProbe.IsSuccess ? driverProbe.Value.DriverName : null,
            teamProbe.IsSuccess ? teamProbe.Value.TeamName : null,
            numberProbe.IsSuccess ? numberProbe.Value.CarNumber : null);
        return result.IsSuccess ? result.Value : null;
    }

    private static bool TryReadOptionalLap(IracingFrameSnapshot snapshot, out LapNumber? lap)
    {
        if (!snapshot.Variables.ContainsKey(IracingProtocol.LapVariable))
        {
            lap = null;
            return true;
        }

        if (!TryReadInt32(snapshot, IracingProtocol.LapVariable, out var value))
        {
            lap = null;
            return false;
        }

        if (value < 0)
        {
            lap = null;
            return true;
        }

        var result = LapNumber.TryCreate(value);
        lap = result.IsSuccess ? result.Value : null;
        return result.IsSuccess;
    }

    internal static bool TryConvertSessionMilliseconds(double seconds, out long milliseconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            milliseconds = default;
            return false;
        }

        var flooredMilliseconds = Math.Floor(seconds * 1000d);
        if (!double.IsFinite(flooredMilliseconds) ||
            flooredMilliseconds < 0 ||
            flooredMilliseconds > long.MaxValue / TimeSpan.TicksPerMillisecond)
        {
            milliseconds = default;
            return false;
        }

        milliseconds = (long)flooredMilliseconds;
        return true;
    }

    internal static bool IsReplayMode(bool isReplayPlaying, int cameraState) =>
        isReplayPlaying ||
        (cameraState & (int)IracingCameraState.IsSessionScreen) != 0;

    private static bool TryReadOptionalLapDistance(
        IracingFrameSnapshot snapshot,
        out LapDistance? lapDistance)
    {
        if (!snapshot.Variables.ContainsKey(IracingProtocol.LapDistanceVariable))
        {
            lapDistance = null;
            return true;
        }

        if (!TryReadSingle(snapshot, IracingProtocol.LapDistanceVariable, out var value))
        {
            lapDistance = null;
            return false;
        }

        if (value < 0)
        {
            lapDistance = null;
            return true;
        }

        var result = LapDistance.TryCreate(value);
        lapDistance = result.IsSuccess ? result.Value : null;
        return result.IsSuccess;
    }

    internal static bool TryReadInt32(
        IracingFrameSnapshot snapshot,
        string name,
        out int value)
    {
        if (!TryGetScalar(snapshot, name, IracingVariableType.Integer, sizeof(int), out var bytes))
        {
            value = default;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    internal static bool TryReadDouble(
        IracingFrameSnapshot snapshot,
        string name,
        out double value)
    {
        if (!TryGetScalar(snapshot, name, IracingVariableType.Double, sizeof(double), out var bytes))
        {
            value = default;
            return false;
        }

        value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
        return true;
    }

    private static bool TryReadSingle(
        IracingFrameSnapshot snapshot,
        string name,
        out float value)
    {
        if (!TryGetScalar(snapshot, name, IracingVariableType.Single, sizeof(float), out var bytes))
        {
            value = default;
            return false;
        }

        value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        return true;
    }

    internal static bool TryReadBoolean(
        IracingFrameSnapshot snapshot,
        string name,
        out bool value)
    {
        if (!TryGetScalar(snapshot, name, IracingVariableType.Boolean, sizeof(byte), out var bytes) ||
            bytes[0] is not (0 or 1))
        {
            value = default;
            return false;
        }

        value = bytes[0] == 1;
        return true;
    }

    internal static bool TryReadBitField(
        IracingFrameSnapshot snapshot,
        string name,
        out int value)
    {
        if (!TryGetScalar(snapshot, name, IracingVariableType.BitField, sizeof(int), out var bytes))
        {
            value = default;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryGetScalar(
        IracingFrameSnapshot snapshot,
        string name,
        IracingVariableType type,
        int size,
        out ReadOnlySpan<byte> bytes)
    {
        if (!snapshot.Variables.TryGetValue(name, out var variable) ||
            variable.Type != type ||
            variable.Count != 1 ||
            variable.Offset < 0 ||
            (long)variable.Offset + size > snapshot.Frame.Length)
        {
            bytes = default;
            return false;
        }

        bytes = snapshot.Frame.AsSpan(variable.Offset, size);
        return true;
    }

    private static Result<TelemetrySample> Invalid() =>
        Result<TelemetrySample>.Failure(IracingErrors.InvalidTelemetryFrame);
}
