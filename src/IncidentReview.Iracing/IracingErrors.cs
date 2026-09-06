using IncidentReview.Results;

namespace IncidentReview.Iracing;

internal static class IracingErrors
{
    public static Error InvalidOptions { get; } = Error.Create(
        IracingErrorCodes.InvalidOptions,
        ErrorKind.Validation,
        "The iRacing integration options are invalid.");

    public static Error TelemetryUnavailable { get; } = Error.Create(
        IracingErrorCodes.TelemetryUnavailable,
        ErrorKind.Unavailable,
        "The iRacing telemetry endpoint is unavailable.");

    public static Error InvalidTelemetryFrame { get; } = Error.Create(
        IracingErrorCodes.InvalidTelemetryFrame,
        ErrorKind.Integration,
        "The iRacing telemetry frame is malformed or inconsistent.");

    public static Error TelemetryAlreadyObserved { get; } = Error.Create(
        IracingErrorCodes.TelemetryAlreadyObserved,
        ErrorKind.Conflict,
        "The iRacing telemetry source already has an active observer.");

    public static Error TelemetryBufferOverflow { get; } = Error.Create(
        IracingErrorCodes.TelemetryBufferOverflow,
        ErrorKind.Unavailable,
        "The bounded iRacing telemetry buffer cannot preserve every meaningful transition.");

    public static Error UnsupportedReplayPosition { get; } = Error.Create(
        IracingErrorCodes.UnsupportedReplayPosition,
        ErrorKind.Validation,
        "The replay position cannot be represented by the iRacing SDK protocol.");

    public static Error UnsupportedPlaybackRate { get; } = Error.Create(
        IracingErrorCodes.UnsupportedPlaybackRate,
        ErrorKind.Validation,
        "The replay playback rate cannot be represented exactly by the iRacing SDK protocol.");

    public static Error ReplayUnavailable { get; } = Error.Create(
        IracingErrorCodes.ReplayUnavailable,
        ErrorKind.Unavailable,
        "The iRacing replay-message endpoint is unavailable.");

    public static Error ReplayDeliveryFailed { get; } = Error.Create(
        IracingErrorCodes.ReplayDeliveryFailed,
        ErrorKind.Integration,
        "Windows did not accept the iRacing replay command for delivery.");
}
