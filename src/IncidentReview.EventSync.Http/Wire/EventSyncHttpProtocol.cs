using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;

namespace IncidentReview.EventSync.Http.Wire;

internal static class EventSyncHttpProtocol
{
    public const int SchemaVersion = 1;
    public const string RouteTemplate = "/event-sync/v1/sessions/{sessionIdentity}/events";

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Uri CreatePublishUri(JoinCode joinCode) => new(
        joinCode.ServerBaseUri,
        string.Concat(
            "event-sync/v1/sessions/",
            joinCode.SessionIdentity.ToString(),
            "/events"));

    public static WireCustomEventRequest ToWire(CustomEventSubmission submission) => new(
        SchemaVersion,
        submission.Id.ToString(),
        submission.SessionIdentity.ToString(),
        submission.ReplayPosition.SessionNumber.Value,
        submission.ReplayPosition.SessionTime.Milliseconds,
        CustomEventId.EventType,
        submission.Submitter.Value,
        submission.OccurredAt.UnixMilliseconds);

    public static CustomEventSubmission? TryFromWire(WireCustomEventRequest? wire)
    {
        if (wire is null ||
            wire.SchemaVersion != SchemaVersion ||
            wire.EventId is null ||
            wire.SessionIdentity is null ||
            wire.ReplaySessionNumber is null ||
            wire.ReplaySessionTimeMilliseconds is null ||
            wire.OccurredAtUnixMilliseconds is null ||
            !string.Equals(wire.EventType, CustomEventId.EventType, StringComparison.Ordinal))
        {
            return null;
        }

        var eventId = CustomEventId.TryParse(wire.EventId);
        var session = SessionIdentity.TryParse(wire.SessionIdentity);
        var sessionNumber = SessionNumber.TryCreate(wire.ReplaySessionNumber.Value);
        var sessionTime = SessionTime.TryCreateMilliseconds(
            wire.ReplaySessionTimeMilliseconds.Value);
        var submitter = SubmitterName.TryCreate(wire.SubmitterName);
        var occurredAt = UtcInstant.TryCreateUnixMilliseconds(
            wire.OccurredAtUnixMilliseconds.Value);
        if (!eventId.IsSuccess ||
            !session.IsSuccess ||
            !sessionNumber.IsSuccess ||
            !sessionTime.IsSuccess ||
            !submitter.IsSuccess ||
            !occurredAt.IsSuccess)
        {
            return null;
        }

        var replayPosition = ReplayPosition.TryCreate(sessionNumber.Value, sessionTime.Value);
        if (!replayPosition.IsSuccess)
        {
            return null;
        }

        var submission = CustomEventSubmission.TryCreate(
            eventId.Value,
            session.Value,
            replayPosition.Value,
            submitter.Value,
            occurredAt.Value);
        return submission.IsSuccess ? submission.Value : null;
    }
}

internal sealed record WireCustomEventRequest(
    int SchemaVersion,
    string? EventId,
    string? SessionIdentity,
    int? ReplaySessionNumber,
    long? ReplaySessionTimeMilliseconds,
    string? EventType,
    string? SubmitterName,
    long? OccurredAtUnixMilliseconds);

internal sealed record WireEventSyncResponse(
    int SchemaVersion,
    string? Outcome,
    string? ErrorCode,
    string? Message);
