using DbUp;
using DbUp.Engine;
using DbUp.Sqlite.Helpers;
using IncidentReview.Results;
using IncidentReview.Store.Sqlite.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteMigrationRunner
{
    private readonly IReadOnlyList<SqlScript> _testMigrations;
    private readonly ILogger _logger;

    public SqliteMigrationRunner(IReadOnlyList<SqlScript> testMigrations, ILogger logger)
    {
        _testMigrations = testMigrations;
        _logger = logger;
    }

    public Result Migrate(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SqlScript> productionScripts;
        try
        {
            productionScripts = MigrationManifest.LoadProductionScripts();
        }
        catch (MigrationManifestException exception)
        {
            SqliteLog.MigrationManifestInvalid(_logger, exception);
            return Result.Failure(SqliteStoreErrors.MigrationManifestInvalid);
        }

        var scripts = productionScripts.Concat(_testMigrations).ToArray();
        using var sharedConnection = new SharedConnection(connection);
        var engine = DeployChanges.To
            .SqliteDatabase(sharedConnection)
            .WithScripts(scripts)
            .WithVariablesDisabled()
            .WithTransaction()
            .LogToNowhere()
            .Build();

        var outcome = engine.PerformUpgrade();
        if (!outcome.Successful)
        {
            SqliteLog.MigrationFailed(_logger, outcome.Error);
            return Result.Failure(SqliteStoreErrors.MigrationFailed);
        }

        return Result.Success();
    }
}
