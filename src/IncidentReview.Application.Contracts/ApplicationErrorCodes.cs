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

    public static ErrorCode ReplayDriverOnTrack { get; } =
        ErrorCode.Define("application.replay.driver-on-track");

    public static ErrorCode ReplayOnTrackStateUnknown { get; } =
        ErrorCode.Define("application.replay.on-track-state-unknown");

    public static ErrorCode ReplayCommandInProgress { get; } =
        ErrorCode.Define("application.replay.command-in-progress");

    public static ErrorCode ReplaySessionNotLoaded { get; } =
        ErrorCode.Define("application.replay.session-not-loaded");

    public static ErrorCode ReplaySessionIdentityUnavailable { get; } =
        ErrorCode.Define("application.replay.session-identity-unavailable");

    public static ErrorCode RuntimeStopped { get; } =
        ErrorCode.Define("application.runtime.stopped");

    public static ErrorCode CustomEventUnavailable { get; } =
        ErrorCode.Define("application.custom-event.unavailable");

    public static ErrorCode CustomEventNameRequired { get; } =
        ErrorCode.Define("application.custom-event.name-required");

    public static ErrorCode CustomEventNotFound { get; } =
        ErrorCode.Define("application.custom-event.not-found");

    public static ErrorCode EventSyncUnavailable { get; } =
        ErrorCode.Define("application.event-sync.unavailable");

    public static ErrorCode CustomEventDuplicate { get; } =
        ErrorCode.Define("application.custom-event.duplicate");

    public static ErrorCode InvalidEventJoinCode { get; } =
        ErrorCode.Define("application.event-sync.join-code.invalid");
}
