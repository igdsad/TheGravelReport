using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>Stable error codes returned by incident-review use cases.</summary>
public static class ApplicationErrorCodes
{
    public static ErrorCode NoCurrentSession { get; } =
        ErrorCode.Define("application.session.none-current");

    public static ErrorCode SessionNotFound { get; } =
        ErrorCode.Define("application.session.not-found");

    public static ErrorCode IncidentNotFound { get; } =
        ErrorCode.Define("application.incident.not-found");

    public static ErrorCode ReplayUnavailable { get; } =
        ErrorCode.Define("application.replay.unavailable");

    public static ErrorCode RuntimeStopped { get; } =
        ErrorCode.Define("application.runtime.stopped");
}
