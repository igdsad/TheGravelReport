using IncidentReview.Results;

namespace IncidentReview.EventSync.Http;

internal static class EventSyncHttpErrors
{
    public static Error InvalidOptions { get; } = Error.Create(
        EventSyncHttpErrorCodes.InvalidOptions,
        ErrorKind.Validation,
        "The HTTP event-sync options are invalid.");

    public static Error PublishIndeterminate { get; } = Error.Create(
        EventSyncHttpErrorCodes.PublishIndeterminate,
        ErrorKind.Indeterminate,
        "The HTTP event-sync request did not return a provable outcome.");

    public static Error RemoteRejected { get; } = Error.Create(
        EventSyncHttpErrorCodes.RemoteRejected,
        ErrorKind.Integration,
        "The event-sync server rejected the custom event.");

    public static Error InvalidResponse { get; } = Error.Create(
        EventSyncHttpErrorCodes.InvalidResponse,
        ErrorKind.Integration,
        "The event-sync server returned an invalid response.");

    public static Error HostUnavailable { get; } = Error.Create(
        EventSyncHttpErrorCodes.HostUnavailable,
        ErrorKind.Unavailable,
        "The local event-sync HTTP host is unavailable.");

    public static Error HostAlreadyRunning { get; } = Error.Create(
        EventSyncHttpErrorCodes.HostAlreadyRunning,
        ErrorKind.Conflict,
        "The event-sync HTTP host is already serving a session.");
}
