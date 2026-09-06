namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Produces an ordered stream of simulator-neutral telemetry state transitions.
/// </summary>
public interface ITelemetrySource
{
    /// <summary>
    /// Observes connection state and validated telemetry samples until cancellation.
    /// </summary>
    /// <remarks>
    /// Expected availability failures are emitted as events. Cancellation ends
    /// enumeration with <see cref="OperationCanceledException"/>; unexpected
    /// defects fault the stream.
    /// </remarks>
    public IAsyncEnumerable<TelemetryEvent> ObserveAsync(
        CancellationToken cancellationToken);
}
