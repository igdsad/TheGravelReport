using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>Safe failures owned by the application boundary.</summary>
public static class ApplicationErrors
{
    public static Error NoCurrentSession { get; } = Error.Create(
        ApplicationErrorCodes.NoCurrentSession,
        ErrorKind.NotFound,
        "No review session is currently selected.");

    public static Error SessionNotFound { get; } = Error.Create(
        ApplicationErrorCodes.SessionNotFound,
        ErrorKind.NotFound,
        "The requested review session was not found.");

    public static Error IncidentNotFound { get; } = Error.Create(
        ApplicationErrorCodes.IncidentNotFound,
        ErrorKind.NotFound,
        "The requested incident was not found.");

    public static Error ReplayUnavailable { get; } = Error.Create(
        ApplicationErrorCodes.ReplayUnavailable,
        ErrorKind.Unavailable,
        "iRacing telemetry is not connected, so replay control is unavailable. Start or reconnect iRacing, then try again.");

    public static Error ReplayDriverOnTrack { get; } = Error.Create(
        ApplicationErrorCodes.ReplayDriverOnTrack,
        ErrorKind.Conflict,
        "iRacing still reports the driver as on track. Being stopped in the pit does not count as being out of the car; use iRacing's tow or exit control, then try again.");

    public static Error ReplayOnTrackStateUnknown { get; } = Error.Create(
        ApplicationErrorCodes.ReplayOnTrackStateUnknown,
        ErrorKind.Unavailable,
        "The app has not received the driver's on-track state from iRacing yet. Wait for telemetry to update, then try again.");

    public static Error ReplayCommandInProgress { get; } = Error.Create(
        ApplicationErrorCodes.ReplayCommandInProgress,
        ErrorKind.Conflict,
        "Another replay request is still in progress. Wait for it to finish, then try again.");

    public static Error ReplaySessionNotLoaded { get; } = Error.Create(
        ApplicationErrorCodes.ReplaySessionNotLoaded,
        ErrorKind.Conflict,
        "The selected incident belongs to an iRacing session that is not currently loaded. Load that session's replay, then try again.");

    public static Error ReplaySessionIdentityUnavailable { get; } = Error.Create(
        ApplicationErrorCodes.ReplaySessionIdentityUnavailable,
        ErrorKind.Conflict,
        "iRacing did not provide a stable ID for this replay, so the app cannot verify that it matches the saved incident. Keep Incident Review running while recording and entering replay, then try again.");

    public static Error RuntimeStopped { get; } = Error.Create(
        ApplicationErrorCodes.RuntimeStopped,
        ErrorKind.Unavailable,
        "The incident-review runtime has stopped.");
}
