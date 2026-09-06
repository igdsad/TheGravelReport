namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Indicates that a previously connected telemetry source is disconnected.
/// </summary>
public sealed class TelemetryDisconnected : TelemetryEvent
{
    private TelemetryDisconnected()
    {
    }

    /// <summary>
    /// Gets the immutable disconnected-state event.
    /// </summary>
    public static TelemetryDisconnected Instance { get; } = new();
}
