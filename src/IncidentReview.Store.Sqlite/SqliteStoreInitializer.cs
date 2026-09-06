using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteStoreInitializer : IStoreInitializer
{
    private readonly SqliteStoreOptions _options;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly SqliteSchemaValidator _schemaValidator;
    private readonly SqliteStoreGate _gate;
    private readonly Action<SqliteConnection>? _configureMigrationConnection;
    private readonly ILogger _logger;
    private readonly object _initializationLock = new();
    private bool _initialized;

    public SqliteStoreInitializer(
        SqliteStoreOptions options,
        IReadOnlyList<DbUp.Engine.SqlScript> testMigrations,
        SqliteStoreGate gate,
        ILogger<SqliteStoreInitializer> logger)
        : this(options, testMigrations, gate, logger, configureMigrationConnection: null)
    {
    }

    internal SqliteStoreInitializer(
        SqliteStoreOptions options,
        IReadOnlyList<DbUp.Engine.SqlScript> testMigrations,
        SqliteStoreGate gate,
        ILogger<SqliteStoreInitializer> logger,
        Action<SqliteConnection>? configureMigrationConnection)
    {
        _options = options;
        _connectionFactory = new SqliteConnectionFactory(options);
        _migrationRunner = new SqliteMigrationRunner(testMigrations, logger);
        _schemaValidator = new SqliteSchemaValidator(options.BusyTimeoutSeconds, logger);
        _gate = gate;
        _configureMigrationConnection = configureMigrationConnection;
        _logger = logger;
    }

    public Task<Result> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_initializationLock)
        {
            if (_initialized)
            {
                return Task.FromResult(Result.Success());
            }

            return Task.FromResult(InitializeCore(cancellationToken));
        }
    }

    private Result InitializeCore(CancellationToken cancellationToken)
    {
        var directoryResult = EnsureDataDirectory();
        if (!directoryResult.IsSuccess)
        {
            return directoryResult;
        }

        var providerResult = EnsureNativeProvider();
        if (!providerResult.IsSuccess)
        {
            return providerResult;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(_options.DatabasePath))
            {
                using var preflightConnection = _connectionFactory.CreateOpenReadOnly();
                var preflightResult = _schemaValidator.ValidateMigrationPreflight(preflightConnection);
                if (!preflightResult.IsSuccess)
                {
                    return preflightResult;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var connection = _connectionFactory.CreateOpenForInitialization();
            _configureMigrationConnection?.Invoke(connection);
            var migrationResult = _migrationRunner.Migrate(connection, cancellationToken);
            if (!migrationResult.IsSuccess)
            {
                return migrationResult;
            }

            var validationResult = _schemaValidator.Validate(connection);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }
        }
        catch (SqliteException exception)
        {
            SqliteLog.InitializationFailed(_logger, exception);
            return Result.Failure(SqliteStoreErrors.MigrationFailed);
        }

        _initialized = true;
        _gate.Open();
        return Result.Success();
    }

    private Result EnsureDataDirectory()
    {
        try
        {
            var directory = Path.GetDirectoryName(_options.DatabasePath);
            if (string.IsNullOrEmpty(directory))
            {
                return Result.Failure(SqliteStoreErrors.DirectoryUnavailable);
            }

            _ = Directory.CreateDirectory(directory);
            return Result.Success();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            SqliteLog.DirectoryUnavailable(_logger, exception);
            return Result.Failure(SqliteStoreErrors.DirectoryUnavailable);
        }
    }

    private Result EnsureNativeProvider()
    {
        try
        {
            SqliteNativeProvider.EnsureInitialized();
            return Result.Success();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or TypeInitializationException)
        {
            SqliteLog.ProviderUnavailable(_logger, exception);
            return Result.Failure(SqliteStoreErrors.ProviderUnavailable);
        }
    }
}
