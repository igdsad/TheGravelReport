using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Http;

internal static class EventSyncHttpLog
{
    private static readonly Action<ILogger, Exception?> LogPublishIndeterminate =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4001, nameof(PublishIndeterminate)),
            "A custom-event HTTP publish ended without a provable server outcome.");

    private static readonly Action<ILogger, int, Exception?> LogRemoteRejected =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(4002, nameof(RemoteRejected)),
            "The custom-event server rejected an HTTP request with status {StatusCode}.");

    private static readonly Action<ILogger, Exception?> LogInvalidResponse =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4003, nameof(InvalidResponse)),
            "The custom-event server returned an invalid HTTP response.");

    private static readonly Action<ILogger, Exception?> LogHostUnavailable =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4004, nameof(HostUnavailable)),
            "The local custom-event HTTP host is unavailable.");

    private static readonly Action<ILogger, Guid, Exception?> LogDuplicateReceived =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(4005, nameof(DuplicateReceived)),
            "Ignored duplicate custom event {EventId}; the first received record remains authoritative.");

    private static readonly Action<ILogger, string, Exception?> LogHandlerRejected =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4006, nameof(HandlerRejected)),
            "The custom-event receiver rejected a request with error {ErrorCode}.");

    private static readonly Action<ILogger, Exception?> LogRequestInvalid =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4007, nameof(RequestInvalid)),
            "The local custom-event HTTP host rejected a malformed request.");

    private static readonly Action<ILogger, Exception?> LogHandlerFaulted =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4008, nameof(HandlerFaulted)),
            "The custom-event receiver failed unexpectedly while handling a request.");

    public static void PublishIndeterminate(ILogger logger, Exception exception) =>
        LogPublishIndeterminate(logger, exception);

    public static void RemoteRejected(ILogger logger, int statusCode) =>
        LogRemoteRejected(logger, statusCode, null);

    public static void InvalidResponse(ILogger logger, Exception? exception = null) =>
        LogInvalidResponse(logger, exception);

    public static void HostUnavailable(ILogger logger, Exception exception) =>
        LogHostUnavailable(logger, exception);

    public static void DuplicateReceived(ILogger logger, Guid eventId) =>
        LogDuplicateReceived(logger, eventId, null);

    public static void HandlerRejected(ILogger logger, string errorCode) =>
        LogHandlerRejected(logger, errorCode, null);

    public static void RequestInvalid(ILogger logger, Exception? exception = null) =>
        LogRequestInvalid(logger, exception);

    public static void HandlerFaulted(ILogger logger, Exception exception) =>
        LogHandlerFaulted(logger, exception);
}
