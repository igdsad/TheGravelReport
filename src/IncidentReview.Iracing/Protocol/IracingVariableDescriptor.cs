namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingVariableDescriptor(
    IracingVariableType Type,
    int Offset,
    int Count,
    string Name);

internal sealed record IracingFrameSnapshot(
    byte[] Frame,
    long? SubSessionId,
    string SessionInfo,
    IReadOnlyDictionary<string, IracingVariableDescriptor> Variables);

internal enum IracingReadStatus
{
    NoData,
    Disconnected,
    Snapshot,
    Invalid,
}

internal sealed record IracingReadResult(
    IracingReadStatus Status,
    IracingFrameSnapshot? Snapshot = null)
{
    public static IracingReadResult NoData { get; } = new(IracingReadStatus.NoData);

    public static IracingReadResult Disconnected { get; } = new(IracingReadStatus.Disconnected);

    public static IracingReadResult Invalid { get; } = new(IracingReadStatus.Invalid);

    public static IracingReadResult FromSnapshot(IracingFrameSnapshot snapshot) =>
        new(IracingReadStatus.Snapshot, snapshot);
}
