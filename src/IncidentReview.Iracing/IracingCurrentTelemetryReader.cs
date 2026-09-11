using IncidentReview.Results;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing;

internal sealed class IracingCurrentTelemetryReader : ICurrentTelemetryReader
{
    private readonly IracingConnectionState _connectionState;

    public IracingCurrentTelemetryReader(IracingConnectionState connectionState)
    {
        _connectionState = connectionState;
    }

    public Result<TelemetrySample> Read()
    {
        var sample = _connectionState.ReadCurrentTelemetry();
        return sample is null
            ? Result<TelemetrySample>.Failure(IracingErrors.TelemetryUnavailable)
            : Result<TelemetrySample>.Success(sample);
    }
}
