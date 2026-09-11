using IncidentReview.Iracing.Options;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Iracing.Replay;
using IncidentReview.Replay.Contracts;
using IncidentReview.Telemetry.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentReview.Iracing.DependencyInjection;

/// <summary>Registers the official local iRacing SDK integration.</summary>
public static class IracingServiceCollectionExtensions
{
    /// <summary>
    /// Adds singleton telemetry and replay adapters using validated options or production defaults.
    /// </summary>
    public static IServiceCollection AddIracingIntegration(
        this IServiceCollection services,
        IracingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= IracingOptions.Default;

        services.TryAddSingleton(options);
        services.TryAddSingleton(static provider =>
            IracingProtocolConfiguration.Production(
                provider.GetRequiredService<IracingOptions>()));
        services.TryAddSingleton<IracingConnectionState>();
        services.TryAddSingleton<IReplayMessageSender, WindowsReplayMessageSender>();
        services.TryAddSingleton<ITelemetrySource>(static provider =>
            new IracingTelemetrySource(
                provider.GetRequiredService<IracingProtocolConfiguration>(),
                provider.GetRequiredService<IracingConnectionState>()));
        services.TryAddSingleton<IReplayController>(static provider =>
            new IracingReplayController(
                provider.GetRequiredService<IReplayMessageSender>(),
                provider.GetRequiredService<IracingConnectionState>()));
        services.TryAddSingleton<IReplayContextReader>(static provider =>
            new IracingReplayContextReader(
                provider.GetRequiredService<IracingConnectionState>()));
        services.TryAddSingleton<ICurrentTelemetryReader>(static provider =>
            new IracingCurrentTelemetryReader(
                provider.GetRequiredService<IracingConnectionState>()));
        return services;
    }
}
