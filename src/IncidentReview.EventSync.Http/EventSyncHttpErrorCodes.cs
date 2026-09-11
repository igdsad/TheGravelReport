using IncidentReview.Results;

namespace IncidentReview.EventSync.Http;

/// <summary>Stable error codes owned by the HTTP custom-event adapter.</summary>
public static class EventSyncHttpErrorCodes
{
    /// <summary>The HTTP adapter configuration is invalid.</summary>
    public static ErrorCode InvalidOptions { get; } =
        ErrorCode.Define("event-sync.http.options-invalid");

    /// <summary>The HTTP request may have reached the server but no outcome was received.</summary>
    public static ErrorCode PublishIndeterminate { get; } =
        ErrorCode.Define("event-sync.http.publish-indeterminate");

    /// <summary>The remote server rejected the request.</summary>
    public static ErrorCode RemoteRejected { get; } =
        ErrorCode.Define("event-sync.http.remote-rejected");

    /// <summary>The remote response was malformed or inconsistent.</summary>
    public static ErrorCode InvalidResponse { get; } =
        ErrorCode.Define("event-sync.http.response-invalid");

    /// <summary>The local HTTP host could not start or stop cleanly.</summary>
    public static ErrorCode HostUnavailable { get; } =
        ErrorCode.Define("event-sync.http.host-unavailable");

    /// <summary>The local HTTP host is already serving a session.</summary>
    public static ErrorCode HostAlreadyRunning { get; } =
        ErrorCode.Define("event-sync.http.host-already-running");
}
