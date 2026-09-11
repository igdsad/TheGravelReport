using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.EventSync.Http.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentReview.EventSync.Http.DependencyInjection;

/// <summary>Maps the bounded version-1 HTTP receiver surface.</summary>
public static class EventSyncHttpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a receiver that accepts custom events for any canonical deterministic UUIDv5 session.
    /// </summary>
    /// <remarks>
    /// An <see cref="ICustomEventReceiver"/> must be registered in the request service provider.
    /// The receiver remains responsible for atomically enforcing first-identity-wins behavior.
    /// </remarks>
    public static IEndpointConventionBuilder MapHttpEventSyncReceiver(
        this IEndpointRouteBuilder endpoints,
        EventSyncHttpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var logger = endpoints.ServiceProvider
            .GetService<ILoggerFactory>()?
            .CreateLogger(typeof(HttpCustomEventRequestHandler).FullName ??
                          nameof(HttpCustomEventRequestHandler)) ??
            NullLogger.Instance;
        var handler = new HttpCustomEventRequestHandler(
            options ?? EventSyncHttpOptions.Default,
            logger);

        return endpoints.MapPost(
            EventSyncHttpProtocol.RouteTemplate,
            (HttpContext context,
             string sessionIdentity,
             ICustomEventReceiver receiver) =>
                handler.HandleAsync(
                    context,
                    sessionIdentity,
                    receiver,
                    hostedSessionIdentity: null));
    }

    internal static IEndpointConventionBuilder MapHostedHttpEventSyncReceiver(
        this IEndpointRouteBuilder endpoints,
        EventSyncHttpOptions options,
        ILogger logger,
        SessionIdentity hostedSessionIdentity,
        ICustomEventReceiver receiver)
    {
        var handler = new HttpCustomEventRequestHandler(options, logger);
        return endpoints.MapPost(
            EventSyncHttpProtocol.RouteTemplate,
            (HttpContext context, string sessionIdentity) =>
                handler.HandleAsync(
                    context,
                    sessionIdentity,
                    receiver,
                    hostedSessionIdentity));
    }
}
