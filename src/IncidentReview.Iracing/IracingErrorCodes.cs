using IncidentReview.Results;

namespace IncidentReview.Iracing;

/// <summary>Stable failure codes owned by the official iRacing integration.</summary>
public static class IracingErrorCodes
{
    /// <summary>The adapter options are outside their supported bounds.</summary>
    public static ErrorCode InvalidOptions { get; } =
        ErrorCode.Define("iracing.options.invalid");

    /// <summary>The simulator telemetry endpoint is not currently available.</summary>
    public static ErrorCode TelemetryUnavailable { get; } =
        ErrorCode.Define("iracing.telemetry.unavailable");

    /// <summary>The shared-memory metadata or copied frame is malformed.</summary>
    public static ErrorCode InvalidTelemetryFrame { get; } =
        ErrorCode.Define("iracing.telemetry.frame-invalid");

    /// <summary>The telemetry source already has an active observer.</summary>
    public static ErrorCode TelemetryAlreadyObserved { get; } =
        ErrorCode.Define("iracing.telemetry.already-observed");

    /// <summary>A meaningful telemetry transition could not enter the bounded buffer.</summary>
    public static ErrorCode TelemetryBufferOverflow { get; } =
        ErrorCode.Define("iracing.telemetry.buffer-overflow");

    /// <summary>The requested replay position cannot be represented by the SDK wire format.</summary>
    public static ErrorCode UnsupportedReplayPosition { get; } =
        ErrorCode.Define("iracing.replay.position-unsupported");

    /// <summary>The requested playback rate cannot be represented without rounding.</summary>
    public static ErrorCode UnsupportedPlaybackRate { get; } =
        ErrorCode.Define("iracing.replay.playback-unsupported");

    /// <summary>The simulator replay-message endpoint is unavailable.</summary>
    public static ErrorCode ReplayUnavailable { get; } =
        ErrorCode.Define("iracing.replay.unavailable");

    /// <summary>The operating system did not accept a replay message for delivery.</summary>
    public static ErrorCode ReplayDeliveryFailed { get; } =
        ErrorCode.Define("iracing.replay.delivery-failed");
}
