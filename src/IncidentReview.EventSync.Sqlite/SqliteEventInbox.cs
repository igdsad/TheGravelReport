using System.Threading.Channels;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Sqlite.Options;
using IncidentReview.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Sqlite;

internal sealed class SqliteEventInbox : ICustomEventReceiver
{
    private const int SchemaVersion = 1;

    private const string InitializeSchemaSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = FULL;

        CREATE TABLE IF NOT EXISTS "EventSyncSchema" (
            "singleton" INTEGER NOT NULL PRIMARY KEY CHECK ("singleton" = 1),
            "schema_version" INTEGER NOT NULL CHECK ("schema_version" > 0)
        ) STRICT;

        INSERT INTO "EventSyncSchema" ("singleton", "schema_version")
        VALUES (1, 1)
        ON CONFLICT ("singleton") DO NOTHING;

        CREATE TABLE IF NOT EXISTS "CustomEventInbox" (
            "custom_event_id" TEXT NOT NULL PRIMARY KEY COLLATE BINARY,
            "session_id" TEXT NOT NULL COLLATE BINARY,
            "replay_session_number" INTEGER NOT NULL CHECK ("replay_session_number" >= 0),
            "session_time_ms" INTEGER NOT NULL CHECK ("session_time_ms" >= 0),
            "submitter_name" TEXT NOT NULL CHECK (length("submitter_name") BETWEEN 1 AND 128),
            "occurred_at_unix_ms" INTEGER NOT NULL
        ) STRICT;

        CREATE INDEX IF NOT EXISTS "IX_CustomEventInbox_Session_Position"
        ON "CustomEventInbox" (
            "session_id",
            "replay_session_number",
            "session_time_ms",
            "custom_event_id");
        """;

    private const string ReadSchemaVersionSql = """
        SELECT "schema_version"
        FROM "EventSyncSchema"
        WHERE "singleton" = 1;
        """;

    private const string InsertEventSql = """
        INSERT INTO "CustomEventInbox" (
            "custom_event_id",
            "session_id",
            "replay_session_number",
            "session_time_ms",
            "submitter_name",
            "occurred_at_unix_ms")
        VALUES (
            $custom_event_id,
            $session_id,
            $replay_session_number,
            $session_time_ms,
            $submitter_name,
            $occurred_at_unix_ms)
        ON CONFLICT ("custom_event_id") DO NOTHING;
        """;

    private readonly SqliteEventInboxOptions _options;
    private readonly SqliteEventInboxConnectionFactory _connectionFactory;
    private readonly Channel<PendingSubmission> _queue;
    private readonly ISqliteEventInboxExecutionCheckpoint _executionCheckpoint;
    private readonly ILogger _logger;
    private int _accepting;
    private int _lifecycleState;

    public SqliteEventInbox(
        SqliteEventInboxOptions options,
        ISqliteEventInboxExecutionCheckpoint executionCheckpoint,
        ILogger logger)
    {
        _options = options;
        _connectionFactory = new SqliteEventInboxConnectionFactory(options);
        _executionCheckpoint = executionCheckpoint;
        _logger = logger;
        _queue = Channel.CreateBounded<PendingSubmission>(
            new BoundedChannelOptions(options.ExecutorCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public async Task<Result<CustomEventPublishOutcome>> ReceiveAsync(
        CustomEventSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _accepting) != 1)
        {
            return Result<CustomEventPublishOutcome>.Failure(SqliteEventInboxErrors.NotReady);
        }

        var pending = new PendingSubmission(submission);
        if (!_queue.Writer.TryWrite(pending))
        {
            var error = Volatile.Read(ref _accepting) == 1
                ? SqliteEventInboxErrors.CapacityExceeded
                : SqliteEventInboxErrors.NotReady;
            return Result<CustomEventPublishOutcome>.Failure(error);
        }

        return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task<Result> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _lifecycleState, 1, 0) != 0)
        {
            var repeatedResult = Volatile.Read(ref _lifecycleState) == 1
                ? Result.Success()
                : Result.Failure(SqliteEventInboxErrors.NotReady);
            return Task.FromResult(repeatedResult);
        }

        var initialization = InitializeDatabase(cancellationToken);
        if (!initialization.IsSuccess)
        {
            Volatile.Write(ref _lifecycleState, 3);
            return Task.FromResult(initialization);
        }

        return Task.FromResult(Result.Success());
    }

    internal void StartAccepting() => Volatile.Write(ref _accepting, 1);

    internal Task RunAsync() => ProcessQueueAsync();

    internal void StopAccepting()
    {
        _ = Interlocked.Exchange(ref _accepting, 0);
        Volatile.Write(ref _lifecycleState, 2);
        _queue.Writer.TryComplete();
    }

    private Result InitializeDatabase(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_options.DatabasePath);
            if (string.IsNullOrEmpty(directory))
            {
                return Result.Failure(SqliteEventInboxErrors.InitializationFailed);
            }

            _ = Directory.CreateDirectory(directory);
            cancellationToken.ThrowIfCancellationRequested();

            using var connection = _connectionFactory.CreateForInitialization();
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = InitializeSchemaSql;
                _ = command.ExecuteNonQuery();
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = ReadSchemaVersionSql;
            var version = versionCommand.ExecuteScalar();
            if (version is not long value || value != SchemaVersion)
            {
                throw new InvalidDataException("The event inbox has an unsupported schema version.");
            }

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            SqliteEventInboxLog.InitializationFailed(_logger, exception);
            return Result.Failure(SqliteEventInboxErrors.InitializationFailed);
        }
    }

    private async Task ProcessQueueAsync()
    {
        PendingSubmission? active = null;
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                active = pending;
                pending.Completion.TrySetResult(Persist(pending.Submission));
                active = null;
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _accepting, 0);
            _queue.Writer.TryComplete();
            SqliteEventInboxLog.WorkerFailed(_logger, exception);
            active?.Completion.TrySetResult(
                Result<CustomEventPublishOutcome>.Failure(
                    SqliteEventInboxErrors.PersistenceUnavailable));
            while (_queue.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetResult(
                    Result<CustomEventPublishOutcome>.Failure(
                        SqliteEventInboxErrors.PersistenceUnavailable));
            }

            throw;
        }
    }

    private Result<CustomEventPublishOutcome> Persist(CustomEventSubmission submission)
    {
        _executionCheckpoint.BeforePersist();
        SqliteConnection? connection = null;
        SqliteTransaction? transaction = null;
        Result<CustomEventPublishOutcome>? outcome = null;
        var commitStarted = false;
        try
        {
            connection = _connectionFactory.CreateForWrite();
            connection.Open();
            transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = InsertEventSql;
            AddParameters(command, submission);
            var changed = command.ExecuteNonQuery();

            commitStarted = true;
            transaction.Commit();
            outcome = Result<CustomEventPublishOutcome>.Success(
                changed == 1
                    ? CustomEventPublishOutcome.Accepted
                    : CustomEventPublishOutcome.Duplicate);
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            if (commitStarted)
            {
                SqliteEventInboxLog.CommitIndeterminate(_logger, exception);
                outcome = Result<CustomEventPublishOutcome>.Failure(
                    SqliteEventInboxErrors.CommitIndeterminate);
            }
            else
            {
                SqliteEventInboxLog.PersistenceFailed(_logger, exception);
                outcome = Result<CustomEventPublishOutcome>.Failure(
                    SqliteEventInboxErrors.PersistenceUnavailable);
            }
        }
        finally
        {
            try
            {
                transaction?.Dispose();
                connection?.Dispose();
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            {
                SqliteEventInboxLog.CleanupFailed(_logger, exception);
            }
        }

        return outcome ?? Result<CustomEventPublishOutcome>.Failure(
            SqliteEventInboxErrors.PersistenceUnavailable);
    }

    private static void AddParameters(
        SqliteCommand command,
        CustomEventSubmission submission)
    {
        _ = command.Parameters.Add(
            new SqliteParameter("$custom_event_id", SqliteType.Text)
            {
                Value = submission.Id.ToString(),
            });
        _ = command.Parameters.Add(
            new SqliteParameter("$session_id", SqliteType.Text)
            {
                Value = submission.SessionIdentity.ToString(),
            });
        _ = command.Parameters.Add(
            new SqliteParameter("$replay_session_number", SqliteType.Integer)
            {
                Value = submission.ReplayPosition.SessionNumber.Value,
            });
        _ = command.Parameters.Add(
            new SqliteParameter("$session_time_ms", SqliteType.Integer)
            {
                Value = submission.ReplayPosition.SessionTime.Milliseconds,
            });
        _ = command.Parameters.Add(
            new SqliteParameter("$submitter_name", SqliteType.Text)
            {
                Value = submission.Submitter.Value,
            });
        _ = command.Parameters.Add(
            new SqliteParameter("$occurred_at_unix_ms", SqliteType.Integer)
            {
                Value = submission.OccurredAt.UnixMilliseconds,
            });
    }

    private static bool IsPersistenceException(Exception exception) =>
        exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or NotSupportedException;

    private sealed class PendingSubmission
    {
        public PendingSubmission(CustomEventSubmission submission)
        {
            Submission = submission;
            Completion = new TaskCompletionSource<Result<CustomEventPublishOutcome>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public CustomEventSubmission Submission { get; }

        public TaskCompletionSource<Result<CustomEventPublishOutcome>> Completion { get; }
    }
}
