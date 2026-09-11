using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.EventSync.Http.Wire;
using IncidentReview.Results;
using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Http;

internal sealed class HttpCustomEventPublisher : ICustomEventPublisher
{
    private readonly HttpClient _httpClient;
    private readonly EventSyncHttpOptions _options;
    private readonly ILogger _logger;

    public HttpCustomEventPublisher(
        HttpClient httpClient,
        EventSyncHttpOptions options,
        ILogger logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<CustomEventPublishOutcome>> PublishAsync(
        JoinCode joinCode,
        CustomEventSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(joinCode);
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();

        if (joinCode.SessionIdentity != submission.SessionIdentity)
        {
            return Result<CustomEventPublishOutcome>.Failure(EventSyncErrors.SessionMismatch);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            EventSyncHttpProtocol.CreatePublishUri(joinCode))
        {
            Content = JsonContent.Create(
                EventSyncHttpProtocol.ToWire(submission),
                options: EventSyncHttpProtocol.SerializerOptions),
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            EventSyncHttpLog.PublishIndeterminate(_logger, exception);
            return Result<CustomEventPublishOutcome>.Failure(
                EventSyncHttpErrors.PublishIndeterminate);
        }
        catch (HttpRequestException exception)
        {
            EventSyncHttpLog.PublishIndeterminate(_logger, exception);
            return Result<CustomEventPublishOutcome>.Failure(
                EventSyncHttpErrors.PublishIndeterminate);
        }
        catch (IOException exception)
        {
            EventSyncHttpLog.PublishIndeterminate(_logger, exception);
            return Result<CustomEventPublishOutcome>.Failure(
                EventSyncHttpErrors.PublishIndeterminate);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                EventSyncHttpLog.RemoteRejected(_logger, (int)response.StatusCode);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.RemoteRejected);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                EventSyncHttpLog.InvalidResponse(_logger);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.InvalidResponse);
            }

            WireEventSyncResponse? wireResponse;
            try
            {
                var body = await ReadBoundedBodyAsync(
                    response.Content,
                    _options.MaximumResponseBodyBytes,
                    timeout.Token);
                if (body is null)
                {
                    EventSyncHttpLog.InvalidResponse(_logger);
                    return Result<CustomEventPublishOutcome>.Failure(
                        EventSyncHttpErrors.InvalidResponse);
                }

                wireResponse = JsonSerializer.Deserialize<WireEventSyncResponse>(
                    body,
                    EventSyncHttpProtocol.SerializerOptions);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                EventSyncHttpLog.PublishIndeterminate(_logger, exception);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.PublishIndeterminate);
            }
            catch (JsonException exception)
            {
                EventSyncHttpLog.InvalidResponse(_logger, exception);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.InvalidResponse);
            }
            catch (IOException exception)
            {
                EventSyncHttpLog.PublishIndeterminate(_logger, exception);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.PublishIndeterminate);
            }
            catch (HttpRequestException exception)
            {
                EventSyncHttpLog.PublishIndeterminate(_logger, exception);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.PublishIndeterminate);
            }

            if (!TryMapOutcome(response.StatusCode, wireResponse, out var outcome))
            {
                EventSyncHttpLog.InvalidResponse(_logger);
                return Result<CustomEventPublishOutcome>.Failure(
                    EventSyncHttpErrors.InvalidResponse);
            }

            return Result<CustomEventPublishOutcome>.Success(outcome);
        }
    }

    private static bool TryMapOutcome(
        HttpStatusCode statusCode,
        WireEventSyncResponse? response,
        out CustomEventPublishOutcome outcome)
    {
        outcome = default;
        if (response is null ||
            response.SchemaVersion != EventSyncHttpProtocol.SchemaVersion ||
            response.ErrorCode is not null ||
            response.Message is not null)
        {
            return false;
        }

        if (statusCode == HttpStatusCode.Created &&
            string.Equals(response.Outcome, "accepted", StringComparison.Ordinal))
        {
            outcome = CustomEventPublishOutcome.Accepted;
            return true;
        }

        if (statusCode == HttpStatusCode.OK &&
            string.Equals(response.Outcome, "duplicate", StringComparison.Ordinal))
        {
            outcome = CustomEventPublishOutcome.Duplicate;
            return true;
        }

        return false;
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            return null;
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
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
