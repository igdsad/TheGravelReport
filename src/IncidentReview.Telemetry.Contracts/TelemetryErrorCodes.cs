using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Stable error codes owned by the telemetry contract boundary.
/// </summary>
public static class TelemetryErrorCodes
{
    /// <summary>The supplied values cannot form a consistent telemetry sample.</summary>
    public static ErrorCode InvalidSample { get; } =
        ErrorCode.Define("telemetry.sample.invalid");
}
