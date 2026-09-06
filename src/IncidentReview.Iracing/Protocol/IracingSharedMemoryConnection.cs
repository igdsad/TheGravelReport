using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace IncidentReview.Iracing.Protocol;

internal sealed class IracingSharedMemoryConnection : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly WindowsDataValidEvent _dataEvent;
    private readonly TimeProvider _timeProvider;
    private readonly Action? _betweenSessionInformationCopies;
    private VariableMetadataCache? _variableMetadata;
    private SessionInformationCache? _sessionInformation;
    private int? _lastTick;
    private long? _lastValidTimestamp;
    private int _variableMetadataDecodeCount;
    private int _sessionInformationDecodeCount;
    private int _isDisposed;

    private IracingSharedMemoryConnection(
        MemoryMappedFile mapping,
        MemoryMappedViewAccessor view,
        WindowsDataValidEvent dataEvent,
        TimeProvider timeProvider,
        Action? betweenSessionInformationCopies)
    {
        _mapping = mapping;
        _view = view;
        _dataEvent = dataEvent;
        _timeProvider = timeProvider;
        _betweenSessionInformationCopies = betweenSessionInformationCopies;
        _lastValidTimestamp = timeProvider.GetTimestamp();
    }

    public static bool TryOpen(
        string memoryMapName,
        string dataValidEventName,
        TimeProvider timeProvider,
        out IracingSharedMemoryConnection? connection,
        Action? betweenSessionInformationCopies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryMapName);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataValidEventName);
        ArgumentNullException.ThrowIfNull(timeProvider);

        MemoryMappedFile? mapping = null;
        MemoryMappedViewAccessor? view = null;
        WindowsDataValidEvent? dataEvent = null;
        try
        {
            mapping = MemoryMappedFile.OpenExisting(memoryMapName, MemoryMappedFileRights.Read);
            view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (!WindowsDataValidEvent.TryOpen(dataValidEventName, out dataEvent))
            {
                view.Dispose();
                mapping.Dispose();
                connection = null;
                return false;
            }

            connection = new IracingSharedMemoryConnection(
                mapping,
                view,
                dataEvent,
                timeProvider,
                betweenSessionInformationCopies);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            dataEvent?.Dispose();
            view?.Dispose();
            mapping?.Dispose();
            connection = null;
            return false;
        }
    }

    public IracingReadResult TryRead()
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = TryReadOnce();
            if (result is not IracingReadResult.NoData)
            {
                return result;
            }
        }

        return HasTimedOut()
            ? IracingReadResult.Disconnected.Instance
            : IracingReadResult.NoData.Instance;
    }

    public async ValueTask WaitForDataAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveTimeout = timeout;
        if (_lastValidTimestamp is { } timestamp)
        {
            var elapsed = _timeProvider.GetElapsedTime(
                timestamp,
                _timeProvider.GetTimestamp());
            var remaining = IracingProtocol.ConnectionTimeout - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (remaining < effectiveTimeout)
            {
                effectiveTimeout = remaining;
            }
        }

        var timeoutMilliseconds = checked((int)Math.Ceiling(effectiveTimeout.TotalMilliseconds));
        var signaledIndex = await Task.Run(
            () => WaitHandle.WaitAny(
                [_dataEvent, cancellationToken.WaitHandle],
                timeoutMilliseconds),
            CancellationToken.None).ConfigureAwait(false);

        if (signaledIndex == 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    internal int VariableMetadataDecodeCount =>
        Volatile.Read(ref _variableMetadataDecodeCount);

    internal int SessionInformationDecodeCount =>
        Volatile.Read(ref _sessionInformationDecodeCount);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _dataEvent.Dispose();
        _view.Dispose();
        _mapping.Dispose();
    }

    private IracingReadResult TryReadOnce()
    {
        if (_view.Capacity < IracingProtocol.HeaderSize)
        {
            return IracingReadResult.Invalid.Instance;
        }

        var header = ReadBytes(0, IracingProtocol.HeaderSize);
        if (header is null)
        {
            return IracingReadResult.Invalid.Instance;
        }

        var status = ReadInt32(header, 4);
        if ((status & IracingProtocol.ConnectedStatus) == 0)
        {
            return IracingReadResult.Disconnected.Instance;
        }

        var version = ReadInt32(header, 0);
        var sessionInfoUpdate = ReadInt32(header, 12);
        var sessionInfoLength = ReadInt32(header, 16);
        var sessionInfoOffset = ReadInt32(header, 20);
        var numberOfVariables = ReadInt32(header, 24);
        var variableHeaderOffset = ReadInt32(header, 28);
        var numberOfBuffers = ReadInt32(header, 32);
        var bufferLength = ReadInt32(header, 36);
        var currentBuffer = header[IracingProtocol.CurrentBufferOffset];

        if (version != IracingProtocol.Version ||
            numberOfBuffers is < 1 or > IracingProtocol.MaximumBuffers ||
            currentBuffer >= numberOfBuffers ||
            numberOfVariables is < 1 or > IracingProtocol.MaximumVariables ||
            bufferLength is < 1 or > IracingProtocol.MaximumBufferLength ||
            sessionInfoLength is < 0 or > IracingProtocol.MaximumSessionInfoLength ||
            !IsRangeValid(sessionInfoOffset, sessionInfoLength) ||
            !TryMultiply(numberOfVariables, IracingProtocol.VariableHeaderSize, out var headersLength) ||
            !IsRangeValid(variableHeaderOffset, headersLength))
        {
            return IracingReadResult.Invalid.Instance;
        }

        var bufferDescriptorOffset = checked(
            IracingProtocol.VariableBufferOffset +
            (currentBuffer * IracingProtocol.VariableBufferSize));
        var tickCount = ReadInt32(header, bufferDescriptorOffset);
        var bufferOffset = ReadInt32(header, bufferDescriptorOffset + 4);

        if (!IsRangeValid(bufferOffset, bufferLength))
        {
            return IracingReadResult.Invalid.Instance;
        }

        if (_lastTick == tickCount)
        {
            return IracingReadResult.NoData.Instance;
        }

        if (_lastTick is not null && tickCount < _lastTick)
        {
            _lastTick = tickCount;
            return IracingReadResult.NoData.Instance;
        }

        Thread.MemoryBarrier();

        var cachedVariableMetadata = _variableMetadata;
        var variableMetadataCacheHit = cachedVariableMetadata?.Matches(
            sessionInfoUpdate,
            numberOfVariables,
            variableHeaderOffset,
            bufferLength) == true;
        byte[]? variableHeaders = null;
        if (!variableMetadataCacheHit)
        {
            variableHeaders = ReadBytes(variableHeaderOffset, headersLength);
            if (variableHeaders is null)
            {
                return IracingReadResult.Invalid.Instance;
            }

            Thread.MemoryBarrier();
            var confirmation = ReadBytes(variableHeaderOffset, headersLength);
            if (confirmation is null)
            {
                return IracingReadResult.Invalid.Instance;
            }

            if (!variableHeaders.AsSpan().SequenceEqual(confirmation))
            {
                return IracingReadResult.NoData.Instance;
            }

            variableHeaders = confirmation;
        }

        var cachedSessionInformation = _sessionInformation;
        var sessionInformationCacheHit = cachedSessionInformation?.Matches(
            sessionInfoUpdate,
            sessionInfoOffset,
            sessionInfoLength) == true;
        byte[]? sessionInfoBytes = null;
        if (!sessionInformationCacheHit)
        {
            sessionInfoBytes = ReadBytes(sessionInfoOffset, sessionInfoLength);
            if (sessionInfoBytes is null)
            {
                return IracingReadResult.Invalid.Instance;
            }

            _betweenSessionInformationCopies?.Invoke();
            Thread.MemoryBarrier();
            var confirmation = ReadBytes(sessionInfoOffset, sessionInfoLength);
            if (confirmation is null)
            {
                return IracingReadResult.Invalid.Instance;
            }

            if (!sessionInfoBytes.AsSpan().SequenceEqual(confirmation))
            {
                return IracingReadResult.NoData.Instance;
            }

            sessionInfoBytes = confirmation;
        }

        var frame = ReadBytes(bufferOffset, bufferLength);
        if ((!variableMetadataCacheHit && variableHeaders is null) ||
            (!sessionInformationCacheHit && sessionInfoBytes is null) ||
            frame is null)
        {
            return IracingReadResult.Invalid.Instance;
        }

        Thread.MemoryBarrier();

        if (!TryReadInt32(0, out var versionAfterCopy) ||
            !TryReadInt32(4, out var statusAfterCopy) ||
            !TryReadInt32(16, out var sessionLengthAfterCopy) ||
            !TryReadInt32(20, out var sessionOffsetAfterCopy) ||
            !TryReadInt32(24, out var variableCountAfterCopy) ||
            !TryReadInt32(28, out var variableOffsetAfterCopy) ||
            !TryReadInt32(32, out var bufferCountAfterCopy) ||
            !TryReadInt32(36, out var bufferLengthAfterCopy) ||
            !TryReadInt32(bufferDescriptorOffset, out var tickAfterCopy) ||
            !TryReadInt32(bufferDescriptorOffset + 4, out var bufferOffsetAfterCopy) ||
            !TryReadInt32(bufferDescriptorOffset + 8, out var tickBeginAfterCopy) ||
            !TryReadInt32(IracingProtocol.SessionInfoUpdateOffset, out var sessionUpdateAfterCopy) ||
            !TryReadByte(IracingProtocol.CurrentBufferOffset, out var currentBufferAfterCopy) ||
            version != versionAfterCopy ||
            status != statusAfterCopy ||
            sessionInfoLength != sessionLengthAfterCopy ||
            sessionInfoOffset != sessionOffsetAfterCopy ||
            numberOfVariables != variableCountAfterCopy ||
            variableHeaderOffset != variableOffsetAfterCopy ||
            numberOfBuffers != bufferCountAfterCopy ||
            bufferLength != bufferLengthAfterCopy ||
            bufferOffset != bufferOffsetAfterCopy ||
            tickCount != tickAfterCopy ||
            tickCount != tickBeginAfterCopy ||
            sessionInfoUpdate != sessionUpdateAfterCopy ||
            currentBuffer != currentBufferAfterCopy)
        {
            return IracingReadResult.NoData.Instance;
        }

        IReadOnlyDictionary<string, IracingVariableDescriptor> variables;
        if (variableHeaders is not null)
        {
            if (!TryDecodeVariableHeaders(variableHeaders, bufferLength, out variables))
            {
                return IracingReadResult.Invalid.Instance;
            }

            _variableMetadata = new VariableMetadataCache(
                sessionInfoUpdate,
                numberOfVariables,
                variableHeaderOffset,
                bufferLength,
                variables);
            _ = Interlocked.Increment(ref _variableMetadataDecodeCount);
        }
        else
        {
            variables = cachedVariableMetadata!.Variables;
        }

        long? subSessionId;
        string sessionInfo;
        if (sessionInfoBytes is not null)
        {
            if (!TryDecodeSessionInformation(
                    sessionInfoBytes,
                    out subSessionId,
                    out sessionInfo))
            {
                return IracingReadResult.Invalid.Instance;
            }

            _sessionInformation = new SessionInformationCache(
                sessionInfoUpdate,
                sessionInfoOffset,
                sessionInfoLength,
                subSessionId,
                sessionInfo);
            _ = Interlocked.Increment(ref _sessionInformationDecodeCount);
        }
        else
        {
            subSessionId = cachedSessionInformation!.SubSessionId;
            sessionInfo = cachedSessionInformation.SessionInfo;
        }

        if (variables.Count != numberOfVariables)
        {
            return IracingReadResult.Invalid.Instance;
        }

        _lastTick = tickCount;
        _lastValidTimestamp = _timeProvider.GetTimestamp();
        return new IracingReadResult.Snapshot(
            new IracingFrameSnapshot(frame, subSessionId, sessionInfo, variables));
    }

    private static bool TryDecodeVariableHeaders(
        byte[] bytes,
        int bufferLength,
        out IReadOnlyDictionary<string, IracingVariableDescriptor> variables)
    {
        var result = new Dictionary<string, IracingVariableDescriptor>(StringComparer.Ordinal);
        for (var offset = 0; offset < bytes.Length; offset += IracingProtocol.VariableHeaderSize)
        {
            var typeValue = ReadInt32(bytes, offset);
            var valueOffset = ReadInt32(bytes, offset + 4);
            var count = ReadInt32(bytes, offset + 8);
            if (!Enum.IsDefined(typeof(IracingVariableType), typeValue) || count < 1)
            {
                variables = result;
                return false;
            }

            var type = (IracingVariableType)typeValue;
            if (!TryGetElementSize(type, out var elementSize) ||
                !TryMultiply(count, elementSize, out var valueLength) ||
                valueOffset < 0 ||
                (long)valueOffset + valueLength > bufferLength ||
                !TryDecodeName(bytes.AsSpan(offset + 16, 32), out var name))
            {
                variables = result;
                return false;
            }

            if (!result.TryAdd(
                    name,
                    new IracingVariableDescriptor(type, valueOffset, count, name)))
            {
                variables = result;
                return false;
            }
        }

        variables = result;
        return true;
    }

    private static bool TryGetElementSize(IracingVariableType type, out int size)
    {
        size = type switch
        {
            IracingVariableType.Character or IracingVariableType.Boolean => 1,
            IracingVariableType.Integer or IracingVariableType.BitField or
                IracingVariableType.Single => 4,
            IracingVariableType.Double => 8,
            _ => 0,
        };
        return size != 0;
    }

    private static bool TryDecodeName(ReadOnlySpan<byte> bytes, out string name)
    {
        var terminator = bytes.IndexOf((byte)0);
        var value = terminator >= 0 ? bytes[..terminator] : bytes;
        if (value.IsEmpty)
        {
            name = string.Empty;
            return false;
        }

        foreach (var character in value)
        {
            if (!((character is >= (byte)'A' and <= (byte)'Z') ||
                  (character is >= (byte)'a' and <= (byte)'z') ||
                  (character is >= (byte)'0' and <= (byte)'9') ||
                  character == (byte)'_'))
            {
                name = string.Empty;
                return false;
            }
        }

        name = Encoding.ASCII.GetString(value);
        return true;
    }

    private static bool TryDecodeSessionInformation(
        byte[] bytes,
        out long? subSessionId,
        out string sessionInfo)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        if (bytes.AsSpan(0, length).Contains((byte)0))
        {
            subSessionId = null;
            sessionInfo = string.Empty;
            return false;
        }

        var sessionInfoBytes = bytes.AsSpan(0, length);
        sessionInfo = Encoding.Latin1.GetString(sessionInfoBytes);
        try
        {
            if (IracingSessionInfoDecoder.UsesUtf8Encoding(sessionInfo))
            {
                sessionInfo = StrictUtf8.GetString(sessionInfoBytes);
            }
        }
        catch (DecoderFallbackException)
        {
            subSessionId = null;
            sessionInfo = string.Empty;
            return false;
        }

        subSessionId = IracingSessionInfoDecoder.TryGetSubSessionId(sessionInfo);
        return true;
    }

    private byte[]? ReadBytes(int offset, int length)
    {
        if (!IsRangeValid(offset, length))
        {
            return null;
        }

        var bytes = new byte[length];
        return _view.ReadArray(offset, bytes, 0, length) == length ? bytes : null;
    }

    private bool TryReadInt32(int offset, out int value)
    {
        if (!IsRangeValid(offset, sizeof(int)))
        {
            value = default;
            return false;
        }

        value = _view.ReadInt32(offset);
        return true;
    }

    private bool TryReadByte(int offset, out byte value)
    {
        if (!IsRangeValid(offset, sizeof(byte)))
        {
            value = default;
            return false;
        }

        value = _view.ReadByte(offset);
        return true;
    }

    private bool IsRangeValid(int offset, int length) =>
        offset >= 0 && length >= 0 && (long)offset + length <= _view.Capacity;

    private bool HasTimedOut() =>
        _lastValidTimestamp is { } timestamp &&
        _timeProvider.GetElapsedTime(timestamp, _timeProvider.GetTimestamp()) >=
            IracingProtocol.ConnectionTimeout;

    private static bool TryMultiply(int left, int right, out int product)
    {
        var value = (long)left * right;
        if (value is < 0 or > int.MaxValue)
        {
            product = default;
            return false;
        }

        product = (int)value;
        return true;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private sealed record VariableMetadataCache(
        int SessionInformationUpdate,
        int VariableCount,
        int HeaderOffset,
        int BufferLength,
        IReadOnlyDictionary<string, IracingVariableDescriptor> Variables)
    {
        public bool Matches(
            int sessionInformationUpdate,
            int variableCount,
            int headerOffset,
            int bufferLength) =>
            SessionInformationUpdate == sessionInformationUpdate &&
            VariableCount == variableCount &&
            HeaderOffset == headerOffset &&
            BufferLength == bufferLength;
    }

    private sealed record SessionInformationCache(
        int Update,
        int Offset,
        int Length,
        long? SubSessionId,
        string SessionInfo)
    {
        public bool Matches(int update, int offset, int length) =>
            Update == update && Offset == offset && Length == length;
    }
}
