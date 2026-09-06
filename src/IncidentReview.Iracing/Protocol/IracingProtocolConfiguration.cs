using IncidentReview.Iracing.Options;

namespace IncidentReview.Iracing.Protocol;

internal sealed record IracingProtocolConfiguration(
    string MemoryMapName,
    string DataValidEventName,
    IracingOptions Options,
    TimeProvider TimeProvider,
    Action? StableFrameCopied = null)
{
    public static IracingProtocolConfiguration Production(IracingOptions options) => new(
        IracingProtocol.MemoryMapName,
        IracingProtocol.DataValidEventName,
        options,
        TimeProvider.System);
}
