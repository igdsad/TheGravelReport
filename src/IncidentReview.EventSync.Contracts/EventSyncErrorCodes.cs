using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>Stable error codes owned by the custom-event synchronization contract.</summary>
public static class EventSyncErrorCodes
{
    /// <summary>The join code is malformed or contains an unsupported value.</summary>
    public static ErrorCode InvalidJoinCode { get; } =
        ErrorCode.Define("event-sync.join-code.invalid");

    /// <summary>The custom-event submission is malformed or inconsistent.</summary>
    public static ErrorCode InvalidSubmission { get; } =
        ErrorCode.Define("event-sync.submission.invalid");

    /// <summary>The hosted-session request is malformed or inconsistent.</summary>
    public static ErrorCode InvalidHostRequest { get; } =
        ErrorCode.Define("event-sync.host-request.invalid");

    /// <summary>The submitted event belongs to a different session.</summary>
    public static ErrorCode SessionMismatch { get; } =
        ErrorCode.Define("event-sync.session.mismatch");
}
