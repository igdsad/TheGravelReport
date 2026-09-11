using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>Reads the latest accepted simulator-neutral telemetry sample.</summary>
public interface ICurrentTelemetryReader
{
    /// <summary>
    /// Reads the latest sample while the simulator telemetry connection is available.
    /// </summary>
    public Result<TelemetrySample> Read();
}
