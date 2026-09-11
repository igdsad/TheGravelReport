using Dapper;
using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal sealed partial class SqliteStore
{
    private const string SelectCustomEventSql =
        """
        SELECT custom_event_id AS CustomEventId,
               session_id AS SessionId,
               replay_session_number AS ReplaySessionNumber,
               replay_session_time_ms AS ReplaySessionTimeMilliseconds,
               submitter_name AS SubmitterName,
               occurred_at_utc_ms AS OccurredAtUnixMilliseconds,
               synchronized_at_utc_ms AS SynchronizedAtUnixMilliseconds
        FROM CustomEvent
        """;

    private const string SelectCustomEventsSql =
        """
        SELECT custom_event_id AS CustomEventId,
               session_id AS SessionId,
               replay_session_number AS ReplaySessionNumber,
               replay_session_time_ms AS ReplaySessionTimeMilliseconds,
               submitter_name AS SubmitterName,
               occurred_at_utc_ms AS OccurredAtUnixMilliseconds,
               synchronized_at_utc_ms AS SynchronizedAtUnixMilliseconds
        FROM CustomEvent
        WHERE session_id = @SessionId
        ORDER BY replay_session_number, replay_session_time_ms, custom_event_id;
        """;

    private const string SelectPendingCustomEventsSql =
        """
        SELECT custom_event_id AS CustomEventId,
               session_id AS SessionId,
               replay_session_number AS ReplaySessionNumber,
               replay_session_time_ms AS ReplaySessionTimeMilliseconds,
               submitter_name AS SubmitterName,
               occurred_at_utc_ms AS OccurredAtUnixMilliseconds,
               synchronized_at_utc_ms AS SynchronizedAtUnixMilliseconds
        FROM CustomEvent
        WHERE session_id = @SessionId
          AND synchronized_at_utc_ms IS NULL
        ORDER BY replay_session_number, replay_session_time_ms, custom_event_id;
        """;

    private Result<T> ReadCustomEventQuery<T>(IStoreQuery<T> query, CancellationToken cancellationToken)
        where T : notnull
    {
        try
        {
            using var connection = _connectionFactory.CreateOpen();
            using var transaction = connection.BeginTransaction();
            cancellationToken.ThrowIfCancellationRequested();
            Result<T> result = query switch
            {
                GetCustomEvent one => ConvertResult<T, StoreLookup<StoredCustomEvent>>(
                    QueryCustomEvent(connection, transaction, one.CustomEvent)),
                GetCustomEvents all => ConvertResult<T, IReadOnlyList<StoredCustomEvent>>(
                    QueryCustomEvents(connection, transaction, all.Session, pendingOnly: false)),
                GetPendingCustomEvents pending => ConvertResult<T, IReadOnlyList<StoredCustomEvent>>(
                    QueryCustomEvents(connection, transaction, pending.Session, pendingOnly: true)),
                _ => Result<T>.Failure(StoreErrors.UnsupportedRequest),
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsSuccess)
            {
                transaction.Commit();
            }

            return result;
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            SqliteLog.StoreOperationFailed(_logger, exception);
            return Result<T>.Failure(StoreErrors.PersistenceFailure);
        }
    }

    private static Result<StoreLookup<StoredCustomEvent>> QueryCustomEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CustomEventId id)
    {
        var row = connection.QuerySingleOrDefault<CustomEventRow>(
            SelectCustomEventSql + " WHERE custom_event_id = @CustomEventId;",
            new { CustomEventId = id.ToString() },
            transaction: transaction);
        if (row is null)
        {
            return Result<StoreLookup<StoredCustomEvent>>.Success(
                StoreLookup.Missing<StoredCustomEvent>());
        }

        var mapped = MapCustomEvent(row);
        return mapped.IsSuccess
            ? Result<StoreLookup<StoredCustomEvent>>.Success(StoreLookup.Found(mapped.Value))
            : Result<StoreLookup<StoredCustomEvent>>.Failure(mapped.Error!);
    }

    private static Result<IReadOnlyList<StoredCustomEvent>> QueryCustomEvents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionIdentity session,
        bool pendingOnly)
    {
        var rows = pendingOnly
            ? connection.Query<CustomEventRow>(
                SelectPendingCustomEventsSql,
                new { SessionId = session.ToString() },
                transaction: transaction)
            : connection.Query<CustomEventRow>(
                SelectCustomEventsSql,
                new { SessionId = session.ToString() },
                transaction: transaction);
        var events = new List<StoredCustomEvent>();
        foreach (var row in rows)
        {
            var mapped = MapCustomEvent(row);
            if (!mapped.IsSuccess)
            {
                return Result<IReadOnlyList<StoredCustomEvent>>.Failure(mapped.Error!);
            }

            events.Add(mapped.Value);
        }

        return Result<IReadOnlyList<StoredCustomEvent>>.Success(events.AsReadOnly());
    }

    private static Result RecordCustomEventCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordCustomEvent command,
        CancellationToken cancellationToken)
    {
        var customEvent = command.CustomEvent;
        cancellationToken.ThrowIfCancellationRequested();
        _ = connection.Execute(
            """
            INSERT INTO CustomEvent (
                custom_event_id, session_id, replay_session_number,
                replay_session_time_ms, submitter_name, occurred_at_utc_ms,
                synchronized_at_utc_ms)
            VALUES (
                @CustomEventId, @SessionId, @ReplaySessionNumber,
                @ReplaySessionTime, @SubmitterName, @OccurredAt, NULL)
            ON CONFLICT(custom_event_id) DO NOTHING;
            """,
            new
            {
                CustomEventId = customEvent.Id.ToString(),
                SessionId = customEvent.Session.ToString(),
                ReplaySessionNumber = customEvent.Position.SessionNumber.Value,
                ReplaySessionTime = customEvent.Position.SessionTime.Milliseconds,
                SubmitterName = customEvent.Submitter.Value,
                OccurredAt = customEvent.OccurredAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success();
    }

    private static Result MarkCustomEventSynchronizedCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MarkCustomEventSynchronized command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = connection.Execute(
            """
            UPDATE CustomEvent
            SET synchronized_at_utc_ms = COALESCE(synchronized_at_utc_ms, @SynchronizedAt)
            WHERE custom_event_id = @CustomEventId;
            """,
            new
            {
                CustomEventId = command.CustomEvent.ToString(),
                SynchronizedAt = command.SynchronizedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1 ? Result.Success() : Result.Failure(StoreErrors.EntityNotFound);
    }

    private static Result RecordReceivedCustomEventCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordReceivedCustomEvent command,
        CancellationToken cancellationToken)
    {
        var customEvent = command.CustomEvent;
        var synchronization = (CustomEventSynchronization.Synchronized)
            customEvent.Synchronization;
        cancellationToken.ThrowIfCancellationRequested();
        var changed = connection.Execute(
            """
            INSERT INTO CustomEvent (
                custom_event_id, session_id, replay_session_number,
                replay_session_time_ms, submitter_name, occurred_at_utc_ms,
                synchronized_at_utc_ms)
            VALUES (
                @CustomEventId, @SessionId, @ReplaySessionNumber,
                @ReplaySessionTime, @SubmitterName, @OccurredAt, @SynchronizedAt)
            ON CONFLICT(custom_event_id) DO NOTHING;
            """,
            new
            {
                CustomEventId = customEvent.Id.ToString(),
                SessionId = customEvent.Session.ToString(),
                ReplaySessionNumber = customEvent.Position.SessionNumber.Value,
                ReplaySessionTime = customEvent.Position.SessionTime.Milliseconds,
                SubmitterName = customEvent.Submitter.Value,
                OccurredAt = customEvent.OccurredAt.UnixMilliseconds,
                SynchronizedAt = synchronization.SynchronizedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1
            ? Result.Success()
            : Result.Failure(StoreErrors.CustomEventIdentityConflict);
    }

    private static Result<StoredCustomEvent> MapCustomEvent(CustomEventRow row)
    {
        var id = CustomEventId.TryParse(row.CustomEventId);
        var session = SessionIdentity.TryParse(row.SessionId);
        var number = SessionNumber.TryCreate(row.ReplaySessionNumber);
        var time = SessionTime.TryCreateMilliseconds(row.ReplaySessionTimeMilliseconds);
        var submitter = SubmitterName.TryCreate(row.SubmitterName);
        var occurredAt = UtcInstant.TryCreateUnixMilliseconds(row.OccurredAtUnixMilliseconds);
        var synchronizedAt = row.SynchronizedAtUnixMilliseconds.HasValue
            ? UtcInstant.TryCreateUnixMilliseconds(row.SynchronizedAtUnixMilliseconds.Value)
            : null;
        if (!id.IsSuccess || !session.IsSuccess || !number.IsSuccess || !time.IsSuccess ||
            !submitter.IsSuccess || !occurredAt.IsSuccess ||
            (synchronizedAt is not null && !synchronizedAt.IsSuccess))
        {
            return Result<StoredCustomEvent>.Failure(StoreErrors.PersistenceFailure);
        }

        var position = ReplayPosition.TryCreate(number.Value, time.Value);
        if (!position.IsSuccess)
        {
            return Result<StoredCustomEvent>.Failure(StoreErrors.PersistenceFailure);
        }

        CustomEventSynchronization synchronization = synchronizedAt is null
            ? CustomEventSynchronization.Pending.Instance
            : CustomEventSynchronization.Synchronized.Create(synchronizedAt.Value);
        try
        {
            return Result<StoredCustomEvent>.Success(StoredCustomEvent.Create(
                id.Value, session.Value, position.Value, submitter.Value,
                occurredAt.Value, synchronization));
        }
        catch (ArgumentException)
        {
            return Result<StoredCustomEvent>.Failure(StoreErrors.PersistenceFailure);
        }
    }
}
