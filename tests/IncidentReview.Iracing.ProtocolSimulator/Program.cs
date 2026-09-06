using System.Buffers.Binary;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace IncidentReview.Iracing.ProtocolSimulator;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || args.Any(static argument => string.IsNullOrWhiteSpace(argument)))
        {
            Console.Error.WriteLine("usage: <memory-map-name> <event-name>");
            return 2;
        }

        using var simulator = new ProtocolSimulator(args[0], args[1]);
        simulator.Initialize();
        Console.WriteLine("READY");

        string? command;
        while ((command = Console.ReadLine()) is not null)
        {
            try
            {
                if (string.Equals(command, "exit", StringComparison.Ordinal))
                {
                    Console.WriteLine("OK exit");
                    return 0;
                }

                simulator.Execute(command);
                Console.WriteLine($"OK {command}");
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException or OverflowException)
            {
                Console.WriteLine("ERROR");
            }
        }

        return 0;
    }
}

internal sealed class ProtocolSimulator : IDisposable
{
    private const int Capacity = 4096;
    private const int HeaderSize = 112;
    private const int VariableHeaderSize = 144;
    private const int VariableHeaderOffset = HeaderSize;
    private const int SessionInfoOffset = 1600;
    private const int FrameOffset = 3072;
    private const int FrameLength = 64;
    private const int FrameStride = 128;
    private const int BufferDescriptorOffset = 48;
    private const int BufferDescriptorLength = 16;
    private const int BufferCount = 3;
    private const int VariableCount = 10;

    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _dataEvent;
    private int _incidentCounter = 4;
    private readonly int _teamIncidentCounter = 9;
    private readonly int _lap = 7;
    private readonly int _sessionNumber = 2;
    private int _replaySessionNumber = 3;
    private int _tick = 1;
    private int _currentBuffer;
    private double _sessionSeconds = 12.345678d;
    private double _replaySessionSeconds = 45.6789d;
    private long? _subSessionId = 987_654_321;
    private string _driverName = "René Mutation A";
    private readonly float _lapDistance = 0.25f;
    private readonly bool _isOnTrack = true;
    private bool _isReplayPlaying;
    private bool _writeInvalidUtf8Session;
    private int _sessionInfoUpdate = 1;
    private SessionTextEncoding _sessionTextEncoding = SessionTextEncoding.Utf8;

    public ProtocolSimulator(string mappingName, string eventName)
    {
        _mapping = MemoryMappedFile.CreateNew(
            mappingName,
            Capacity,
            MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
        _dataEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            eventName);
    }

    public void Initialize()
    {
        WriteMetadata();
        WriteSessionInfo();
        WriteFrame();
        WriteInt32(CurrentBufferDescriptorOffset, _tick);
        WriteInt32(CurrentBufferDescriptorOffset + 8, _tick);
        _view.Flush();
    }

    public void Execute(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("A command is required.", nameof(command));
        }

        switch (parts[0])
        {
            case "publish" when parts.Length == 3:
                _isReplayPlaying = false;
                _incidentCounter = ParseInt32(parts[1]);
                _sessionSeconds = ParseDouble(parts[2]);
                Publish();
                break;
            case "replay" when parts.Length == 4:
                _isReplayPlaying = true;
                _replaySessionNumber = ParseInt32(parts[1]);
                _replaySessionSeconds = ParseDouble(parts[2]);
                _incidentCounter = ParseInt32(parts[3]);
                Publish();
                break;
            case "identity" when parts.Length == 2:
                ChangeIdentity(parts[1], _sessionTextEncoding);
                break;
            case "identity-legacy" when parts.Length == 2:
                ChangeIdentity(parts[1], SessionTextEncoding.Latin1);
                break;
            case "identity-utf8" when parts.Length == 2:
                ChangeIdentity(parts[1], SessionTextEncoding.Utf8);
                break;
            case "identity-only" when parts.Length == 2:
                ChangeIdentity(parts[1], _sessionTextEncoding, publish: false);
                break;
            case "mutate-session" when parts.Length == 1:
                _driverName = string.Equals(
                    _driverName,
                    "René Mutation A",
                    StringComparison.Ordinal)
                    ? "René Mutation B"
                    : "René Mutation A";
                WriteSessionInfo(publishVersion: false);
                break;
            case "invalid-utf8-session" when parts.Length == 1:
                _sessionTextEncoding = SessionTextEncoding.Utf8;
                _writeInvalidUtf8Session = true;
                _sessionInfoUpdate = checked(_sessionInfoUpdate + 1);
                WriteSessionInfo();
                Publish();
                break;
            case "invalid-bool" when parts.Length == 1:
                Publish(isOnTrackOverride: 2);
                break;
            case "nan-time" when parts.Length == 1:
                _sessionSeconds = double.NaN;
                _isReplayPlaying = false;
                Publish();
                break;
            case "torn" when parts.Length == 1:
                _tick = checked(_tick + 1);
                WriteInt32(CurrentBufferDescriptorOffset, _tick);
                _view.Flush();
                _dataEvent.Set();
                break;
            case "tick-regression" when parts.Length == 1:
                _tick = checked(_tick - 1_000);
                WriteInt32(CurrentBufferDescriptorOffset, _tick);
                WriteInt32(CurrentBufferDescriptorOffset + 8, _tick);
                WriteInt32(40, _tick);
                _view.Flush();
                _dataEvent.Set();
                break;
            case "malformed" when parts.Length == 1:
                WriteInt32(32, 5);
                _view.Flush();
                _dataEvent.Set();
                break;
            case "repair" when parts.Length == 1:
                WriteMetadata();
                Publish();
                break;
            case "disconnect" when parts.Length == 1:
                WriteInt32(4, 0);
                _view.Flush();
                _dataEvent.Set();
                break;
            case "reconnect" when parts.Length == 1:
                WriteInt32(4, 1);
                Publish();
                break;
            default:
                throw new ArgumentException("The simulator command is invalid.", nameof(command));
        }
    }

    public void Dispose()
    {
        _dataEvent.Dispose();
        _view.Dispose();
        _mapping.Dispose();
    }

    private void Publish(byte? isOnTrackOverride = null)
    {
        _tick = checked(_tick + 1);
        _currentBuffer = (_currentBuffer + 1) % BufferCount;
        WriteInt32(CurrentBufferDescriptorOffset + 8, _tick);
        WriteFrame(isOnTrackOverride);
        Thread.MemoryBarrier();
        WriteInt32(CurrentBufferDescriptorOffset, _tick);
        WriteInt32(40, _tick);
        _view.Write(44, checked((byte)_currentBuffer));
        _view.Flush();
        _dataEvent.Set();
    }

    private void WriteMetadata()
    {
        WriteInt32(0, 2);
        WriteInt32(4, 1);
        WriteInt32(8, 60);
        WriteInt32(12, _sessionInfoUpdate);
        WriteInt32(16, 0);
        WriteInt32(20, SessionInfoOffset);
        WriteInt32(24, VariableCount);
        WriteInt32(28, VariableHeaderOffset);
        WriteInt32(32, BufferCount);
        WriteInt32(36, FrameLength);
        WriteInt32(40, _tick);
        _view.Write(44, checked((byte)_currentBuffer));
        for (var index = 0; index < BufferCount; index++)
        {
            var descriptorOffset = BufferDescriptorOffset + (index * BufferDescriptorLength);
            var bufferTick = index == _currentBuffer ? _tick : _tick - 1;
            WriteInt32(descriptorOffset, bufferTick);
            WriteInt32(descriptorOffset + 4, FrameOffset + (index * FrameStride));
            WriteInt32(descriptorOffset + 8, bufferTick);
            WriteInt32(descriptorOffset + 12, 0);
        }

        WriteVariableHeader(0, type: 2, valueOffset: 0, "SessionNum");
        WriteVariableHeader(1, type: 5, valueOffset: 8, "SessionTime");
        WriteVariableHeader(2, type: 2, valueOffset: 16, "PlayerCarMyIncidentCount");
        WriteVariableHeader(3, type: 2, valueOffset: 20, "PlayerCarTeamIncidentCount");
        WriteVariableHeader(4, type: 2, valueOffset: 24, "Lap");
        WriteVariableHeader(5, type: 4, valueOffset: 28, "LapDistPct");
        WriteVariableHeader(6, type: 1, valueOffset: 32, "IsOnTrack");
        WriteVariableHeader(7, type: 1, valueOffset: 33, "IsReplayPlaying");
        WriteVariableHeader(8, type: 2, valueOffset: 36, "ReplaySessionNum");
        WriteVariableHeader(9, type: 5, valueOffset: 40, "ReplaySessionTime");
    }

    private void WriteSessionInfo(bool publishVersion = true)
    {
        var encodingLine = _sessionTextEncoding == SessionTextEncoding.Utf8
            ? " Encoding: UTF8\n"
            : string.Empty;
        var text = _subSessionId is null
            ? $"WeekendInfo:\n{encodingLine} TrackName: Test Track\n DriverName: {_driverName}\n"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"WeekendInfo:\n{encodingLine} TrackName: Test Track\n DriverName: {_driverName}\n SubSessionID: {_subSessionId.Value}\n");
        var encoding = _sessionTextEncoding == SessionTextEncoding.Utf8 &&
            !_writeInvalidUtf8Session
            ? Encoding.UTF8
            : Encoding.Latin1;
        var textBytes = encoding.GetBytes(text);
        var bytes = new byte[textBytes.Length + 1];
        textBytes.CopyTo(bytes, 0);
        if (bytes.Length > FrameOffset - SessionInfoOffset)
        {
            throw new InvalidOperationException("The simulator session info exceeds its region.");
        }

        var clear = new byte[FrameOffset - SessionInfoOffset];
        _view.WriteArray(SessionInfoOffset, clear, 0, clear.Length);
        _view.WriteArray(SessionInfoOffset, bytes, 0, bytes.Length);
        if (publishVersion)
        {
            WriteInt32(12, _sessionInfoUpdate);
        }

        WriteInt32(16, bytes.Length);
    }

    private void ChangeIdentity(
        string value,
        SessionTextEncoding encoding,
        bool publish = true)
    {
        _subSessionId = string.Equals(value, "none", StringComparison.Ordinal)
            ? null
            : ParseInt64(value);
        _sessionTextEncoding = encoding;
        _writeInvalidUtf8Session = false;
        _sessionInfoUpdate = checked(_sessionInfoUpdate + 1);
        WriteSessionInfo();
        if (publish)
        {
            Publish();
        }
    }

    private void WriteFrame(byte? isOnTrackOverride = null)
    {
        var frame = new byte[FrameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), _sessionNumber);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(8, 8),
            BitConverter.DoubleToInt64Bits(_sessionSeconds));
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), _incidentCounter);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), _teamIncidentCounter);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24, 4), _lap);
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(28, 4),
            BitConverter.SingleToInt32Bits(_lapDistance));
        frame[32] = isOnTrackOverride ?? (_isOnTrack ? (byte)1 : (byte)0);
        frame[33] = _isReplayPlaying ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36, 4), _replaySessionNumber);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(40, 8),
            BitConverter.DoubleToInt64Bits(_replaySessionSeconds));
        _view.WriteArray(CurrentFrameOffset, frame, 0, frame.Length);
    }

    private void WriteVariableHeader(int index, int type, int valueOffset, string name)
    {
        var offset = checked(VariableHeaderOffset + (index * VariableHeaderSize));
        var bytes = new byte[VariableHeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), valueOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1);
        var nameBytes = Encoding.ASCII.GetBytes(name);
        nameBytes.CopyTo(bytes, 16);
        _view.WriteArray(offset, bytes, 0, bytes.Length);
    }

    private void WriteInt32(int offset, int value) => _view.Write(offset, value);

    private int CurrentBufferDescriptorOffset =>
        BufferDescriptorOffset + (_currentBuffer * BufferDescriptorLength);

    private int CurrentFrameOffset => FrameOffset + (_currentBuffer * FrameStride);

    private static int ParseInt32(string value) =>
        int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static long ParseInt64(string value) =>
        long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private enum SessionTextEncoding
    {
        Utf8,
        Latin1,
    }
}
