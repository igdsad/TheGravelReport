namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Reports one validated telemetry sample from a connected source.
/// </summary>
public sealed class TelemetrySampleObserved : TelemetryEvent
{
    private TelemetrySampleObserved(TelemetrySample sample)
    {
        Sample = sample;
    }

    /// <summary>Gets the immutable observed sample.</summary>
    public TelemetrySample Sample { get; }

    /// <summary>Creates an event around a validated telemetry sample.</summary>
    public static TelemetrySampleObserved Create(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return new TelemetrySampleObserved(sample);
    }
}
