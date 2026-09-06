namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Indicates that the telemetry source has established a usable connection.
/// </summary>
public sealed class TelemetryConnected : TelemetryEvent
{
    private TelemetryConnected()
    {
    }

    /// <summary>
    /// Gets the immutable connection-state event.
    /// </summary>
    public static TelemetryConnected Instance { get; } = new();
}
