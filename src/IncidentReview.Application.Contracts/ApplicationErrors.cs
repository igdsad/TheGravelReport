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
        "Replay control requires iRacing to be connected, the driver out of the car, and the selected incident's session loaded.");

    public static Error RuntimeStopped { get; } = Error.Create(
        ApplicationErrorCodes.RuntimeStopped,
        ErrorKind.Unavailable,
        "The incident-review runtime has stopped.");
}
