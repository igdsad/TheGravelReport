using IncidentReview.Results;

namespace IncidentReview.Iracing.Options;

/// <summary>Validated timing configuration for the local iRacing SDK adapter.</summary>
public sealed record IracingOptions
{
    private const int MinimumEventBufferCapacity = 2;
    private const int MaximumEventBufferCapacity = 4096;
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(1);

    private IracingOptions(
        TimeSpan reconnectInterval,
        TimeSpan dataWaitTimeout,
        int eventBufferCapacity)
    {
        ReconnectInterval = reconnectInterval;
        DataWaitTimeout = dataWaitTimeout;
        EventBufferCapacity = eventBufferCapacity;
    }

    /// <summary>
    /// Gets production defaults: one-second reconnect, 100 ms data wait, and 256 queued events.
    /// </summary>
    public static IracingOptions Default { get; } = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(100),
        eventBufferCapacity: 256);

    /// <summary>Gets the delay between failed shared-memory connection attempts.</summary>
    public TimeSpan ReconnectInterval { get; }

    /// <summary>Gets the maximum wait before the source rechecks shared-memory state.</summary>
    public TimeSpan DataWaitTimeout { get; }

    /// <summary>
    /// Gets the bounded count of pending meaningful telemetry events; at least two preserves
    /// the connected event and its first sample as one logical startup transition.
    /// </summary>
    public int EventBufferCapacity { get; }

    /// <summary>Validates adapter timing values read from a configuration boundary.</summary>
    public static Result<IracingOptions> TryCreate(
        TimeSpan reconnectInterval,
        TimeSpan dataWaitTimeout,
        int eventBufferCapacity = 256) =>
        IsValidInterval(reconnectInterval) &&
        IsValidInterval(dataWaitTimeout) &&
        eventBufferCapacity is >= MinimumEventBufferCapacity and <= MaximumEventBufferCapacity
            ? Result<IracingOptions>.Success(new IracingOptions(
                reconnectInterval,
                dataWaitTimeout,
                eventBufferCapacity))
            : Result<IracingOptions>.Failure(IracingErrors.InvalidOptions);

    private static bool IsValidInterval(TimeSpan value) =>
        value >= TimeSpan.FromMilliseconds(1) &&
        value <= MaximumInterval &&
        value.Ticks % TimeSpan.TicksPerMillisecond == 0;
}
