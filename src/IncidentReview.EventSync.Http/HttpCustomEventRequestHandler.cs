using System.Text.Json;
using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.EventSync.Http.Wire;
using IncidentReview.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Http;

internal sealed class HttpCustomEventRequestHandler(
    EventSyncHttpOptions options,
    ILogger logger)
{
    public async Task HandleAsync(
        HttpContext context,
        string routeSessionIdentity,
        ICustomEventReceiver receiver,
        SessionIdentity? hostedSessionIdentity)
    {
        if (!HasJsonContentType(context.Request.ContentType) ||
            (context.Request.ContentLength is long contentLength &&
             contentLength > options.MaximumRequestBodyBytes))
        {
            EventSyncHttpLog.RequestInvalid(logger);
            await WriteInvalidSubmissionAsync(context);
            return;
        }

        var routeSession = SessionIdentity.TryParse(routeSessionIdentity);
        if (!routeSession.IsSuccess ||
            routeSession.Value.Value.Version != 5 ||
            (hostedSessionIdentity is not null &&
             routeSession.Value != hostedSessionIdentity))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status404NotFound,
                EventSyncErrorCodes.SessionMismatch,
                "The requested event-sync session is not hosted here.");
            return;
        }

        WireCustomEventRequest? wire;
        try
        {
            var requestBody = await ReadBoundedBodyAsync(
                context.Request.Body,
                options.MaximumRequestBodyBytes,
                context.RequestAborted);
            if (requestBody is null)
            {
                EventSyncHttpLog.RequestInvalid(logger);
                await WriteInvalidSubmissionAsync(context);
                return;
            }

            wire = JsonSerializer.Deserialize<WireCustomEventRequest>(
                requestBody,
                EventSyncHttpProtocol.SerializerOptions);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (JsonException exception)
        {
            EventSyncHttpLog.RequestInvalid(logger, exception);
            await WriteInvalidSubmissionAsync(context);
            return;
        }
        catch (BadHttpRequestException exception)
        {
            EventSyncHttpLog.RequestInvalid(logger, exception);
            await WriteInvalidSubmissionAsync(context);
            return;
        }
        catch (IOException exception)
        {
            EventSyncHttpLog.RequestInvalid(logger, exception);
            await WriteInvalidSubmissionAsync(context);
            return;
        }

        var submission = EventSyncHttpProtocol.TryFromWire(wire);
        if (submission is null || submission.SessionIdentity != routeSession.Value)
        {
            EventSyncHttpLog.RequestInvalid(logger);
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                submission is null
                    ? EventSyncErrorCodes.InvalidSubmission
                    : EventSyncErrorCodes.SessionMismatch,
                "The custom-event request is invalid.");
            return;
        }

        Result<CustomEventPublishOutcome> result;
        try
        {
            result = await receiver.ReceiveAsync(submission, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            EventSyncHttpLog.HandlerFaulted(logger, exception);
            throw;
        }

        if (!result.IsSuccess)
        {
            var error = result.Error
                ?? throw new InvalidOperationException("A failed receive result had no error.");
            EventSyncHttpLog.HandlerRejected(logger, error.Code.Value);
            await WriteErrorAsync(
                context,
                MapStatusCode(error.Kind),
                error.Code,
                error.Message);
            return;
        }

        switch (result.Value)
        {
            case CustomEventPublishOutcome.Accepted:
                await WriteOutcomeAsync(
                    context,
                    StatusCodes.Status201Created,
                    "accepted");
                break;
            case CustomEventPublishOutcome.Duplicate:
                EventSyncHttpLog.DuplicateReceived(logger, submission.Id.Value);
                await WriteOutcomeAsync(
                    context,
                    StatusCodes.Status200OK,
                    "duplicate");
                break;
            default:
                throw new InvalidOperationException(
                    "The custom-event receiver returned an undefined outcome.");
        }
    }

    private static bool HasJsonContentType(string? contentType) =>
        contentType is not null &&
        (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) ||
         contentType.StartsWith("application/json;", StringComparison.OrdinalIgnoreCase));

    private static int MapStatusCode(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Persistence => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Integration => StatusCodes.Status502BadGateway,
        ErrorKind.Indeterminate => StatusCodes.Status503ServiceUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined error kind."),
    };

    private static Task WriteInvalidSubmissionAsync(HttpContext context) =>
        WriteErrorAsync(
            context,
            StatusCodes.Status400BadRequest,
            EventSyncErrorCodes.InvalidSubmission,
            "The custom-event request is invalid.");

    private static Task WriteOutcomeAsync(
        HttpContext context,
        int statusCode,
        string outcome) =>
        WriteResponseAsync(
            context,
            statusCode,
            new WireEventSyncResponse(
                EventSyncHttpProtocol.SchemaVersion,
                outcome,
                ErrorCode: null,
                Message: null));

    private static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        ErrorCode errorCode,
        string safeMessage) =>
        WriteResponseAsync(
            context,
            statusCode,
            new WireEventSyncResponse(
                EventSyncHttpProtocol.SchemaVersion,
                Outcome: null,
                errorCode.Value,
                safeMessage));

    private static async Task WriteResponseAsync(
        HttpContext context,
        int statusCode,
        WireEventSyncResponse response)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            EventSyncHttpProtocol.SerializerOptions,
            context.RequestAborted);
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[Math.Min(4096, maximumBytes + 1)];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                return null;
            }

            destination.Write(buffer, 0, read);
        }
    }
}
