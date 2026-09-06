using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite.DependencyInjection;

/// <summary>Registers the SQLite store bootstrap implementation.</summary>
public static class SqliteStoreServiceCollectionExtensions
{
    /// <summary>Adds the validated SQLite store initializer to the supplied service collection.</summary>
    public static IServiceCollection AddSqliteStore(
        this IServiceCollection services,
        SqliteStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _ = services.AddSingleton(options);
        return services.AddSqliteStore();
    }

    /// <summary>
    /// Adds the SQLite store using a <see cref="SqliteStoreOptions"/> instance already
    /// registered in the supplied service collection.
    /// </summary>
    public static IServiceCollection AddSqliteStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<SqliteStoreGate>();
        _ = services.AddSingleton<ISqliteCommitBoundary>(SqliteCommitBoundary.Instance);
        _ = services.AddSingleton<ISqliteTransactionCleanup>(SqliteTransactionCleanup.Instance);
        _ = services.AddSingleton<IStore, SqliteStore>();
        _ = services.AddSingleton<IStoreInitializer>(static provider =>
        {
            var options = provider.GetRequiredService<SqliteStoreOptions>();
            return new SqliteStoreInitializer(
                options,
                Array.Empty<DbUp.Engine.SqlScript>(),
                provider.GetRequiredService<SqliteStoreGate>(),
                provider.GetRequiredService<ILogger<SqliteStoreInitializer>>());
        });
        return services;
    }
}
