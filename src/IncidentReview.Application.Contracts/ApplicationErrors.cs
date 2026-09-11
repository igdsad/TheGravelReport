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
        "iRacing did not provide a stable ID for this replay, so the app cannot verify that it matches the saved incident. Keep the app running while recording and entering replay, then try again.");

    public static Error RuntimeStopped { get; } = Error.Create(
        ApplicationErrorCodes.RuntimeStopped,
        ErrorKind.Unavailable,
        "The incident-review runtime has stopped.");

    public static Error CustomEventUnavailable { get; } = Error.Create(
        ApplicationErrorCodes.CustomEventUnavailable,
        ErrorKind.Unavailable,
        "A live iRacing session and replay position are required to mark a custom event.");

    public static Error CustomEventNameRequired { get; } = Error.Create(
        ApplicationErrorCodes.CustomEventNameRequired,
        ErrorKind.Validation,
        "Enter your name in Custom event settings before marking an event.");

    public static Error CustomEventNotFound { get; } = Error.Create(
        ApplicationErrorCodes.CustomEventNotFound,
        ErrorKind.NotFound,
        "The requested custom event was not found.");

    public static Error EventSyncUnavailable { get; } = Error.Create(
        ApplicationErrorCodes.EventSyncUnavailable,
        ErrorKind.Unavailable,
        "Custom-event network synchronization is unavailable.");

    public static Error CustomEventDuplicate { get; } = Error.Create(
        ApplicationErrorCodes.CustomEventDuplicate,
        ErrorKind.Conflict,
        "A custom event with the same deterministic identity was already received; the first event remains authoritative.");

    public static Error InvalidEventJoinCode { get; } = Error.Create(
        ApplicationErrorCodes.InvalidEventJoinCode,
        ErrorKind.Validation,
        "The custom-event join code is invalid or unsupported.");
}
