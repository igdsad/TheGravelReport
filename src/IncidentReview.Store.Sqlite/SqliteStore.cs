using System.Data;
using System.Security.Cryptography;
using System.Threading.Channels;
using Dapper;
using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal interface ISqliteStoreExecutionCheckpoint
{
    public void AfterOperationLookup();

    public void AfterIncidentInsert();
}

internal sealed class SqliteStoreExecutionCheckpoint : ISqliteStoreExecutionCheckpoint
{
    public static SqliteStoreExecutionCheckpoint Instance { get; } = new();

    private SqliteStoreExecutionCheckpoint()
    {
    }

    public void AfterOperationLookup()
    {
    }

    public void AfterIncidentInsert()
    {
    }
}

internal sealed partial class SqliteStore : IStore, IAsyncDisposable
{
    private const string ReadPreferencesSql =
        """
        SELECT replay_lead_in_ms AS ReplayLeadInMilliseconds,
               auto_pause AS AutoPause,
               playback_speed AS PlaybackSpeed,
               preferred_camera AS PreferredCamera,
               theme_preference AS ThemePreference,
               submitter_name AS SubmitterName,
               custom_event_key AS CustomEventKey,
               event_join_code AS EventJoinCode
        FROM ApplicationPreferences
        WHERE preferences_id = 1;
        """;

    private const string ReadOperationSql =
        """
        SELECT command_kind AS CommandKind,
               command_version AS CommandVersion,
               payload_fingerprint_sha256 AS PayloadFingerprint
        FROM StoreOperation
        WHERE operation_id = @OperationId;
        """;

    private const string OperationExistsSql =
        """
        SELECT EXISTS (
            SELECT 1
            FROM StoreOperation
            WHERE operation_id = @OperationId);
        """;

    private const string UpdatePreferencesSql =
        """
        UPDATE ApplicationPreferences
        SET replay_lead_in_ms = @ReplayLeadInMilliseconds,
            auto_pause = @AutoPause,
            playback_speed = @PlaybackSpeed,
            preferred_camera = @PreferredCamera,
            theme_preference = @ThemePreference,
            submitter_name = @SubmitterName,
            custom_event_key = @CustomEventKey,
            event_join_code = @EventJoinCode,
            updated_at_utc_ms = @UpdatedAtUnixMilliseconds
        WHERE preferences_id = 1;
        """;

    private const string InsertOperationSql =
        """
        INSERT INTO StoreOperation (
            operation_id,
            command_kind,
            command_version,
            payload_fingerprint_sha256,
            committed_at_utc_ms)
        VALUES (
            @OperationId,
            @CommandKind,
            @CommandVersion,
            @PayloadFingerprint,
            @CommittedAtUnixMilliseconds);
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteStoreGate _gate;
    private readonly ISqliteCommitBoundary _commitBoundary;
    private readonly ISqliteTransactionCleanup _transactionCleanup;
    private readonly ISqliteStoreExecutionCheckpoint _executionCheckpoint;
    private readonly ILogger _logger;
    private readonly Channel<StoreWorkItem> _channel;
    private readonly Task _worker;
    private int _disposeStarted;

    public SqliteStore(
        SqliteStoreOptions options,
        SqliteStoreGate gate,
        ISqliteCommitBoundary commitBoundary,
        ISqliteTransactionCleanup transactionCleanup,
        ILogger<SqliteStore> logger)
        : this(
            options,
            gate,
            commitBoundary,
            transactionCleanup,
            SqliteStoreExecutionCheckpoint.Instance,
            logger)
    {
    }

    internal SqliteStore(
        SqliteStoreOptions options,
        SqliteStoreGate gate,
        ISqliteCommitBoundary commitBoundary,
        ISqliteTransactionCleanup transactionCleanup,
        ISqliteStoreExecutionCheckpoint executionCheckpoint,
        ILogger<SqliteStore> logger)
    {
        _connectionFactory = new SqliteConnectionFactory(options);
        _gate = gate;
        _commitBoundary = commitBoundary;
        _transactionCleanup = transactionCleanup;
        _executionCheckpoint = executionCheckpoint;
        _logger = logger;
        _channel = Channel.CreateBounded<StoreWorkItem>(new BoundedChannelOptions(options.ExecutorCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public Task<Result<T>> QueryAsync<T>(
        IStoreQuery<T> query,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return EnqueueQueryAsync(query, cancellationToken);
    }

    public Task<Result> ExecuteAsync(
        IStoreCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return EnqueueCommandAsync(command, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            await _worker.ConfigureAwait(false);
            return;
        }

        _channel.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    private async Task<Result<T>> EnqueueQueryAsync<T>(
        IStoreQuery<T> query,
        CancellationToken cancellationToken)
        where T : notnull
    {
        if (!_gate.IsOpen || Volatile.Read(ref _disposeStarted) != 0)
        {
            return Result<T>.Failure(StoreErrors.NotInitialized);
        }

        var item = new QueryWorkItem<T>(query, cancellationToken);
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return Result<T>.Failure(StoreErrors.NotInitialized);
        }

        return await item.Completion.ConfigureAwait(false);
    }

    private async Task<Result> EnqueueCommandAsync(
        IStoreCommand command,
        CancellationToken cancellationToken)
    {
        if (!_gate.IsOpen || Volatile.Read(ref _disposeStarted) != 0)
        {
            return Result.Failure(StoreErrors.NotInitialized);
        }

        var item = new CommandWorkItem(command, cancellationToken);
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return Result.Failure(StoreErrors.NotInitialized);
        }

        return await item.Completion.ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                item.Run(this);
            }
            catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
            {
                item.Cancel();
            }
            catch (Exception exception)
            {
                SqliteLog.StoreWorkerFailed(_logger, exception);
                item.Fault(exception);
            }
        }
    }

    private Result<T> RunQuery<T>(IStoreQuery<T> query, CancellationToken cancellationToken)
        where T : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query is GetPreferences)
        {
            return ConvertResult<T, UserPreferences>(ReadPreferences(cancellationToken));
        }

        if (query is GetOperationOutcome operationOutcome)
        {
            return ConvertResult<T, OperationOutcome>(ReadOperationOutcome(operationOutcome, cancellationToken));
        }

        if (query is GetSession or
            GetSessionBySimulatorKey or
            ListSessions or
            GetSessionDetails or
            GetIncident or
            GetIncidents or
            GetIncidentCheckpoints)
        {
            return ReadApplicationQuery(query, cancellationToken);
        }

        if (query is GetCustomEvent or GetCustomEvents or GetPendingCustomEvents)
        {
            return ReadCustomEventQuery(query, cancellationToken);
        }

        return Result<T>.Failure(StoreErrors.UnsupportedRequest);
    }

    private Result RunCommand(IStoreCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return command switch
        {
            UpdatePreferences update => WritePreferences(update, cancellationToken),
            EnsureSession or
            PromoteSessionIdentity or
            EstablishIncidentCheckpoint or
            RecordDetectedIncident or
            AnnotateIncident or
            MarkIncidentReviewed => WriteApplicationCommand(command, cancellationToken),
            RecordCustomEvent or
            RecordReceivedCustomEvent or
            MarkCustomEventSynchronized => WriteApplicationCommand(command, cancellationToken),
            _ => Result.Failure(StoreErrors.UnsupportedRequest),
        };
    }

    private Result<UserPreferences> ReadPreferences(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateOpen();
            var transaction = connection.BeginTransaction();
            var transactionCompleted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = connection.QuerySingleOrDefault<PreferencesRow>(
                    ReadPreferencesSql,
                    transaction: transaction);
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null || row.AutoPause is < 0 or > 1)
                {
                    return Result<UserPreferences>.Failure(StoreErrors.PersistenceFailure);
                }

                var preferences = UserPreferences.TryCreateMilliseconds(
                    row.ReplayLeadInMilliseconds,
                    row.PlaybackSpeed,
                    row.AutoPause == 1,
                    row.PreferredCamera,
                    (ThemePreference)row.ThemePreference,
                    row.SubmitterName,
                    row.CustomEventKey,
                    row.EventJoinCode);
                if (!preferences.IsSuccess)
                {
                    return Result<UserPreferences>.Failure(StoreErrors.PersistenceFailure);
                }

                transaction.Commit();
                transactionCompleted = true;
                return preferences;
            }
            finally
            {
                CleanupTransaction(transaction, transactionCompleted);
            }
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            SqliteLog.StoreOperationFailed(_logger, exception);
            return Result<UserPreferences>.Failure(StoreErrors.PersistenceFailure);
        }
    }

    private Result<OperationOutcome> ReadOperationOutcome(
        GetOperationOutcome query,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateOpen();
            var transaction = connection.BeginTransaction();
            var transactionCompleted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var exists = connection.QuerySingle<long>(
                    OperationExistsSql,
                    new { OperationId = query.OperationId.ToString() },
                    transaction: transaction);
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
                transactionCompleted = true;
                return Result<OperationOutcome>.Success(
                    exists == 1 ? OperationOutcome.Committed : OperationOutcome.NotCommitted);
            }
            finally
            {
                CleanupTransaction(transaction, transactionCompleted);
            }
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            SqliteLog.StoreOperationFailed(_logger, exception);
            return Result<OperationOutcome>.Failure(StoreErrors.PersistenceFailure);
        }
    }

    private Result WritePreferences(UpdatePreferences command, CancellationToken cancellationToken)
    {
        var fingerprint = PreferencesCommandFingerprint.Create(command);
        try
        {
            using var connection = _connectionFactory.CreateOpen();
            var transaction = connection.BeginTransaction();
            var transactionCompleted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = connection.QuerySingleOrDefault<StoreOperationRow>(
                    CreateDapperCommand(
                        ReadOperationSql,
                        new { OperationId = command.OperationId.ToString() },
                        transaction,
                        cancellationToken));
                _executionCheckpoint.AfterOperationLookup();
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is not null)
                {
                    return IsSameOperation(
                        operation,
                        PreferencesCommandFingerprint.CommandKind,
                        PreferencesCommandFingerprint.CommandVersion,
                        fingerprint)
                        ? Result.Success()
                        : Result.Failure(StoreErrors.OperationIdConflict);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var changed = connection.Execute(
                    UpdatePreferencesSql,
                    new
                    {
                        command.Preferences.ReplayLeadInMilliseconds,
                        AutoPause = command.Preferences.AutoPause ? 1 : 0,
                        command.Preferences.PlaybackSpeed,
                        command.Preferences.PreferredCamera,
                        ThemePreference = (int)command.Preferences.Theme,
                        command.Preferences.SubmitterName,
                        command.Preferences.CustomEventKey,
                        command.Preferences.EventJoinCode,
                        UpdatedAtUnixMilliseconds = command.UpdatedAt.UnixMilliseconds,
                    },
                    transaction: transaction);
                cancellationToken.ThrowIfCancellationRequested();
                if (changed != 1)
                {
                    return Result.Failure(StoreErrors.PersistenceFailure);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var operationInserted = connection.Execute(
                    InsertOperationSql,
                    new
                    {
                        OperationId = command.OperationId.ToString(),
                        CommandKind = PreferencesCommandFingerprint.CommandKind,
                        CommandVersion = PreferencesCommandFingerprint.CommandVersion,
                        PayloadFingerprint = fingerprint,
                        CommittedAtUnixMilliseconds = command.UpdatedAt.UnixMilliseconds,
                    },
                    transaction: transaction);
                cancellationToken.ThrowIfCancellationRequested();
                if (operationInserted != 1)
                {
                    return Result.Failure(StoreErrors.PersistenceFailure);
                }

                try
                {
                    _commitBoundary.Commit(transaction);
                    transactionCompleted = true;
                }
                catch (Exception exception) when (
                    exception is SqliteException or IndeterminateCommitException)
                {
                    SqliteLog.CommitIndeterminate(_logger, exception);
                    return Result.Failure(StoreErrors.IndeterminateCommit);
                }

                return Result.Success();
            }
            finally
            {
                CleanupTransaction(transaction, transactionCompleted);
            }
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            SqliteLog.StoreOperationFailed(_logger, exception);
            return Result.Failure(StoreErrors.PersistenceFailure);
        }
    }

    private static bool IsSameOperation(
        StoreOperationRow operation,
        string commandKind,
        int commandVersion,
        byte[] fingerprint) =>
        string.Equals(
            operation.CommandKind,
            commandKind,
            StringComparison.Ordinal) &&
        operation.CommandVersion == commandVersion &&
        operation.PayloadFingerprint.Length == fingerprint.Length &&
        CryptographicOperations.FixedTimeEquals(operation.PayloadFingerprint, fingerprint);

    private static CommandDefinition CreateDapperCommand(
        string sql,
        object? parameters,
        SqliteTransaction transaction,
        CancellationToken cancellationToken) => new(
            sql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

    private static bool IsExpectedProviderFailure(Exception exception) =>
        exception is SqliteException or DataException or IOException or UnauthorizedAccessException;

    private void CleanupTransaction(SqliteTransaction transaction, bool transactionCompleted)
    {
        if (!transactionCompleted)
        {
            try
            {
                _transactionCleanup.Rollback(transaction);
            }
            catch (Exception exception)
            {
                SqliteLog.TransactionCleanupFailed(_logger, exception);
            }
        }

        try
        {
            _transactionCleanup.Dispose(transaction);
        }
        catch (Exception exception)
        {
            SqliteLog.TransactionCleanupFailed(_logger, exception);
        }
    }

    private static Result<T> ConvertResult<T, TValue>(Result<TValue> result)
        where T : notnull
        where TValue : notnull
    {
        if (!result.IsSuccess)
        {
            return Result<T>.Failure(result.Error!);
        }

        return result.Value is T value
            ? Result<T>.Success(value)
            : Result<T>.Failure(StoreErrors.UnsupportedRequest);
    }

    private abstract class StoreWorkItem
    {
        protected StoreWorkItem(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public abstract void Run(SqliteStore store);

        public abstract void Cancel();

        public abstract void Fault(Exception exception);
    }

    private sealed class QueryWorkItem<T> : StoreWorkItem
        where T : notnull
    {
        private readonly IStoreQuery<T> _query;
        private readonly TaskCompletionSource<Result<T>> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public QueryWorkItem(IStoreQuery<T> query, CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            _query = query;
        }

        public Task<Result<T>> Completion => _completion.Task;

        public override void Run(SqliteStore store)
        {
            CancellationToken.ThrowIfCancellationRequested();
            _completion.TrySetResult(store.RunQuery(_query, CancellationToken));
        }

        public override void Cancel() => _completion.TrySetCanceled(CancellationToken);

        public override void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class CommandWorkItem : StoreWorkItem
    {
        private readonly IStoreCommand _command;
        private readonly TaskCompletionSource<Result> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CommandWorkItem(IStoreCommand command, CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            _command = command;
        }

        public Task<Result> Completion => _completion.Task;

        public override void Run(SqliteStore store)
        {
            CancellationToken.ThrowIfCancellationRequested();
            _completion.TrySetResult(store.RunCommand(_command, CancellationToken));
        }

        public override void Cancel() => _completion.TrySetCanceled(CancellationToken);

        public override void Fault(Exception exception) => _completion.TrySetException(exception);
    }
}
