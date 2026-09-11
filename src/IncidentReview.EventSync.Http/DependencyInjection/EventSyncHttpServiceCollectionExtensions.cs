using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentReview.EventSync.Http.DependencyInjection;

/// <summary>Registers the replaceable HTTP custom-event synchronization adapter.</summary>
public static class EventSyncHttpServiceCollectionExtensions
{
    /// <summary>Adds singleton publisher and joinable-session host implementations.</summary>
    public static IServiceCollection AddHttpEventSync(
        this IServiceCollection services,
        EventSyncHttpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= EventSyncHttpOptions.Default;

        services.TryAddSingleton(options);
        services.TryAddSingleton(static _ => new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });
        services.TryAddSingleton<ICustomEventPublisher>(static provider =>
            new HttpCustomEventPublisher(
                provider.GetRequiredService<HttpClient>(),
                provider.GetRequiredService<EventSyncHttpOptions>(),
                CreateLogger(provider, typeof(HttpCustomEventPublisher))));
        services.TryAddSingleton<ICustomEventSessionHost>(static provider =>
            new HttpCustomEventSessionHost(
                provider.GetRequiredService<EventSyncHttpOptions>(),
                CreateLogger(provider, typeof(HttpCustomEventSessionHost))));
        return services;
    }

    private static ILogger CreateLogger(IServiceProvider provider, Type category) =>
        provider.GetService<ILoggerFactory>()?.CreateLogger(category.FullName ?? category.Name) ??
        NullLogger.Instance;
}
