using IncidentReview.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentReview.Application.DependencyInjection;

/// <summary>Registers the simulator-neutral application workflow.</summary>
public static class IncidentReviewApplicationServiceCollectionExtensions
{
    /// <summary>Registers one shared incident-review service and runtime singleton.</summary>
    public static IServiceCollection AddIncidentReviewApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        _ = services.AddSingleton<IncidentReviewApplication>();
        _ = services.AddSingleton<IIncidentReviewService>(static provider =>
            provider.GetRequiredService<IncidentReviewApplication>());
        _ = services.AddSingleton<IApplicationRuntime>(static provider =>
            provider.GetRequiredService<IncidentReviewApplication>());
        return services;
    }
}
