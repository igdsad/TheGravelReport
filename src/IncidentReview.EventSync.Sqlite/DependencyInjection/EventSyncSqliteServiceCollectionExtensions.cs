using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentReview.EventSync.Sqlite.DependencyInjection;

/// <summary>Registers the durable server-side SQLite custom-event inbox.</summary>
public static class EventSyncSqliteServiceCollectionExtensions
{
    /// <summary>Adds the inbox receiver and its schema-initialization hosted service.</summary>
    public static IServiceCollection AddSqliteEventSyncReceiver(
        this IServiceCollection services,
        SqliteEventInboxOptions options) => AddSqliteEventSyncReceiver(
            services,
            options,
            SqliteEventInboxExecutionCheckpoint.Instance);

    internal static IServiceCollection AddSqliteEventSyncReceiver(
        IServiceCollection services,
        SqliteEventInboxOptions options,
        ISqliteEventInboxExecutionCheckpoint executionCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executionCheckpoint);

        services.TryAddSingleton(options);
        services.TryAddSingleton(executionCheckpoint);
        services.TryAddSingleton(static provider =>
            new SqliteEventInbox(
                provider.GetRequiredService<SqliteEventInboxOptions>(),
                provider.GetRequiredService<ISqliteEventInboxExecutionCheckpoint>(),
                provider.GetService<ILoggerFactory>()?.CreateLogger<SqliteEventInbox>()
                    ?? NullLogger<SqliteEventInbox>.Instance));
        services.TryAddSingleton<ICustomEventReceiver>(static provider =>
            provider.GetRequiredService<SqliteEventInbox>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, SqliteEventInboxHostedService>());
        return services;
    }
}
