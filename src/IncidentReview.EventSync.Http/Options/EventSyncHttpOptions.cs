using IncidentReview.Results;

namespace IncidentReview.EventSync.Http.Options;

/// <summary>Validated bounds for HTTP event synchronization.</summary>
public sealed record EventSyncHttpOptions
{
    private static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(2);
    private const int MinimumRequestBodyBytes = 1024;
    private const int MaximumRequestBodyBytesLimit = 1024 * 1024;
    private const int MinimumResponseBodyBytes = 256;
    private const int MaximumResponseBodyBytesLimit = 64 * 1024;

    private EventSyncHttpOptions(
        TimeSpan requestTimeout,
        int maximumRequestBodyBytes,
        int maximumResponseBodyBytes)
    {
        RequestTimeout = requestTimeout;
        MaximumRequestBodyBytes = maximumRequestBodyBytes;
        MaximumResponseBodyBytes = maximumResponseBodyBytes;
    }

    /// <summary>Gets production defaults: 15 seconds, 64 KiB requests, and 8 KiB responses.</summary>
    public static EventSyncHttpOptions Default { get; } = new(
        TimeSpan.FromSeconds(15),
        maximumRequestBodyBytes: 64 * 1024,
        maximumResponseBodyBytes: 8 * 1024);

    /// <summary>Gets the per-request network timeout.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Gets the largest JSON request body accepted by the local host.</summary>
    public int MaximumRequestBodyBytes { get; }

    /// <summary>Gets the largest JSON response body accepted by the publisher.</summary>
    public int MaximumResponseBodyBytes { get; }

    /// <summary>Validates HTTP transport bounds read from configuration.</summary>
    public static Result<EventSyncHttpOptions> TryCreate(
        TimeSpan requestTimeout,
        int maximumRequestBodyBytes = 64 * 1024,
        int maximumResponseBodyBytes = 8 * 1024) =>
        requestTimeout >= MinimumRequestTimeout &&
        requestTimeout <= MaximumRequestTimeout &&
        requestTimeout.Ticks % TimeSpan.TicksPerMillisecond == 0 &&
        maximumRequestBodyBytes is >= MinimumRequestBodyBytes and <= MaximumRequestBodyBytesLimit &&
        maximumResponseBodyBytes is >= MinimumResponseBodyBytes and <= MaximumResponseBodyBytesLimit
            ? Result<EventSyncHttpOptions>.Success(new EventSyncHttpOptions(
                requestTimeout,
                maximumRequestBodyBytes,
                maximumResponseBodyBytes))
            : Result<EventSyncHttpOptions>.Failure(EventSyncHttpErrors.InvalidOptions);
}
