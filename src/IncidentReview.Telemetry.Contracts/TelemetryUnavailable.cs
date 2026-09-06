using IncidentReview.Results;

namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Reports an expected transient condition that prevents usable telemetry.
/// </summary>
public sealed class TelemetryUnavailable : TelemetryEvent
{
    private TelemetryUnavailable(Error error)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the safe structured failure reported by the source.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Creates an unavailable event from a validated expected failure.
    /// </summary>
    public static TelemetryUnavailable Create(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new TelemetryUnavailable(error);
    }
}
