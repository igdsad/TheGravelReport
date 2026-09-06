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

internal abstract record IracingReadResult
{
    private IracingReadResult()
    {
    }

    internal sealed record NoData : IracingReadResult
    {
        public static NoData Instance { get; } = new();

        private NoData()
        {
        }
    }

    internal sealed record Disconnected : IracingReadResult
    {
        public static Disconnected Instance { get; } = new();

        private Disconnected()
        {
        }
    }

    internal sealed record Invalid : IracingReadResult
    {
        public static Invalid Instance { get; } = new();

        private Invalid()
        {
        }
    }

    internal sealed record Snapshot : IracingReadResult
    {
        public Snapshot(IracingFrameSnapshot frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            Frame = frame;
        }

        public IracingFrameSnapshot Frame { get; }
    }
}
