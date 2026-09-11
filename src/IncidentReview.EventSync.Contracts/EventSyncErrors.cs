using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>Stable safe failures shared by event-sync implementations.</summary>
public static class EventSyncErrors
{
    /// <summary>Gets the failure for a malformed or unsupported join code.</summary>
    public static Error InvalidJoinCode { get; } = Error.Create(
        EventSyncErrorCodes.InvalidJoinCode,
        ErrorKind.Validation,
        "The event-sync join code is invalid or unsupported.");

    /// <summary>Gets the failure for malformed or inconsistent event data.</summary>
    public static Error InvalidSubmission { get; } = Error.Create(
        EventSyncErrorCodes.InvalidSubmission,
        ErrorKind.Validation,
        "The custom-event submission is invalid.");

    /// <summary>Gets the failure for invalid local or advertised host addresses.</summary>
    public static Error InvalidHostRequest { get; } = Error.Create(
        EventSyncErrorCodes.InvalidHostRequest,
        ErrorKind.Validation,
        "The event-sync host request is invalid.");

    /// <summary>Gets the failure for an event outside the joined session.</summary>
    public static Error SessionMismatch { get; } = Error.Create(
        EventSyncErrorCodes.SessionMismatch,
        ErrorKind.Validation,
        "The custom event does not belong to the joined session.");
}
