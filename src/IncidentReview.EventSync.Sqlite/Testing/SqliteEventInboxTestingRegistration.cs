using IncidentReview.EventSync.Sqlite.DependencyInjection;
using IncidentReview.EventSync.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentReview.EventSync.Sqlite.Testing;

/// <summary>Creates explicit failure seams for SQLite event-inbox integration tests.</summary>
public static class SqliteEventInboxTestingRegistration
{
    /// <summary>Adds an inbox whose worker fails unexpectedly before its first write.</summary>
    public static IServiceCollection AddFaultingSqliteEventSyncReceiver(
        this IServiceCollection services,
        SqliteEventInboxOptions options) =>
        EventSyncSqliteServiceCollectionExtensions.AddSqliteEventSyncReceiver(
            services,
            options,
            ThrowingExecutionCheckpoint.Instance);

    private sealed class ThrowingExecutionCheckpoint : ISqliteEventInboxExecutionCheckpoint
    {
        public static ThrowingExecutionCheckpoint Instance { get; } = new();

        private ThrowingExecutionCheckpoint()
        {
        }

        public void BeforePersist() =>
            throw new ArithmeticException("Injected unexpected inbox-worker failure.");
    }
}
