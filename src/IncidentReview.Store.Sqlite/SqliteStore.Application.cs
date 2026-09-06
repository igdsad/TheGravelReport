using Dapper;
using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal sealed partial class SqliteStore
{
    private const string SelectSessionByIdSql =
        """
        SELECT session_id AS SessionId,
               simulator AS Simulator,
               simulator_session_key AS SimulatorSessionKey,
               identity_kind AS IdentityKind,
               simulator_session_number AS SimulatorSessionNumber,
               session_mode AS SessionMode,
               started_at_utc_ms AS StartedAtUnixMilliseconds
        FROM "Session"
        WHERE session_id = @SessionId;
        """;

    private const string SelectSessionBySimulatorKeySql =
        """
        SELECT session_id AS SessionId,
               simulator AS Simulator,
               simulator_session_key AS SimulatorSessionKey,
               identity_kind AS IdentityKind,
               simulator_session_number AS SimulatorSessionNumber,
               session_mode AS SessionMode,
               started_at_utc_ms AS StartedAtUnixMilliseconds
        FROM "Session"
        WHERE simulator = @Simulator
          AND simulator_session_key = @SessionKey
          AND identity_kind = 1;
        """;

    private const string ListSessionsSql =
        """
        SELECT s.session_id AS SessionId,
               s.simulator AS Simulator,
               s.simulator_session_key AS SimulatorSessionKey,
               s.identity_kind AS IdentityKind,
               s.simulator_session_number AS SimulatorSessionNumber,
               s.session_mode AS SessionMode,
               s.started_at_utc_ms AS StartedAtUnixMilliseconds,
               COUNT(i.incident_id) AS IncidentCount,
               SUM(CASE WHEN i.review_status = 1 THEN 1 ELSE 0 END) AS PendingIncidentCount
        FROM "Session" AS s
        LEFT JOIN Incident AS i ON i.session_id = s.session_id
        GROUP BY s.session_id
        ORDER BY s.started_at_utc_ms DESC, s.session_id DESC;
        """;

    private const string SelectIncidentByIdSql =
        """
        SELECT incident_id AS IncidentId,
               session_id AS SessionId,
               replay_session_number AS ReplaySessionNumber,
               replay_session_time_ms AS ReplaySessionTimeMilliseconds,
               observed_at_utc_ms AS ObservedAtUnixMilliseconds,
               incident_points_delta AS IncidentPointsDelta,
               incident_points_total AS IncidentPointsTotal,
               counter_epoch AS CounterEpoch,
               lap AS Lap,
               lap_distance_percent AS LapDistance,
               review_status AS ReviewStatus,
               classification AS Classification,
               notes AS Notes,
               created_at_utc_ms AS CreatedAtUnixMilliseconds,
               updated_at_utc_ms AS UpdatedAtUnixMilliseconds
        FROM Incident
        WHERE incident_id = @IncidentId;
        """;

    private const string SelectIncidentsSql =
        """
        SELECT incident_id AS IncidentId,
               session_id AS SessionId,
               replay_session_number AS ReplaySessionNumber,
               replay_session_time_ms AS ReplaySessionTimeMilliseconds,
               observed_at_utc_ms AS ObservedAtUnixMilliseconds,
               incident_points_delta AS IncidentPointsDelta,
               incident_points_total AS IncidentPointsTotal,
               counter_epoch AS CounterEpoch,
               lap AS Lap,
               lap_distance_percent AS LapDistance,
               review_status AS ReviewStatus,
               classification AS Classification,
               notes AS Notes,
               created_at_utc_ms AS CreatedAtUnixMilliseconds,
               updated_at_utc_ms AS UpdatedAtUnixMilliseconds
        FROM Incident
        WHERE session_id = @SessionId
        ORDER BY observed_at_utc_ms, incident_id;
        """;

    private const string SelectCheckpointSql =
        """
        SELECT session_id AS SessionId,
               counter_epoch AS CounterEpoch,
               last_incident_points_total AS LastIncidentPointsTotal,
               last_replay_session_number AS LastReplaySessionNumber,
               last_replay_session_time_ms AS LastReplaySessionTimeMilliseconds,
               updated_at_utc_ms AS UpdatedAtUnixMilliseconds
        FROM IncidentCheckpoint
        WHERE session_id = @SessionId;
        """;

    private Result<T> ReadApplicationQuery<T>(
        IStoreQuery<T> query,
        CancellationToken cancellationToken)
        where T : notnull
    {
        try
        {
            using var connection = _connectionFactory.CreateOpen();
            var transaction = connection.BeginTransaction();
            var transactionCompleted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = query switch
                {
                    GetSession getSession => ConvertResult<T, StoreLookup<StoredSession>>(
                        QuerySession(connection, transaction, getSession.Session)),
                    GetSessionBySimulatorKey byKey => ConvertResult<T, StoreLookup<StoredSession>>(
                        QuerySessionBySimulatorKey(connection, transaction, byKey)),
                    ListSessions => ConvertResult<T, IReadOnlyList<StoredSessionSummary>>(
                        QuerySessions(connection, transaction)),
                    GetSessionDetails details => ConvertResult<T, StoreLookup<StoredSessionDetails>>(
                        QuerySessionDetails(connection, transaction, details.Session, cancellationToken)),
                    GetIncident incident => ConvertResult<T, StoreLookup<StoredIncident>>(
                        QueryIncident(connection, transaction, incident.Incident)),
                    GetIncidents incidents => ConvertResult<T, IReadOnlyList<StoredIncident>>(
                        QueryIncidents(connection, transaction, incidents.Session)),
                    GetIncidentCheckpoint checkpoint => ConvertResult<T, StoreLookup<IncidentCheckpoint>>(
                        QueryCheckpoint(connection, transaction, checkpoint.Session)),
                    _ => Result<T>.Failure(StoreErrors.UnsupportedRequest),
                };
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.IsSuccess)
                {
                    return result;
                }

                transaction.Commit();
                transactionCompleted = true;
                return result;
            }
            finally
            {
                CleanupTransaction(transaction, transactionCompleted);
            }
        }
        catch (Exception exception) when (IsExpectedProviderFailure(exception))
        {
            SqliteLog.StoreOperationFailed(_logger, exception);
            return Result<T>.Failure(StoreErrors.PersistenceFailure);
        }
    }

    private static Result<StoreLookup<StoredSession>> QuerySession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionIdentity session)
    {
        var row = connection.QuerySingleOrDefault<SessionRow>(
            SelectSessionByIdSql,
            new { SessionId = session.ToString() },
            transaction: transaction);
        return MapOptionalSession(row);
    }

    private static Result<StoreLookup<StoredSession>> QuerySessionBySimulatorKey(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GetSessionBySimulatorKey query)
    {
        var row = connection.QuerySingleOrDefault<SessionRow>(
            SelectSessionBySimulatorKeySql,
            new { Simulator = query.Simulator.Value, SessionKey = query.SessionKey.Value },
            transaction: transaction);
        return MapOptionalSession(row);
    }

    private static Result<IReadOnlyList<StoredSessionSummary>> QuerySessions(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var rows = connection.Query<SessionSummaryRow>(
            ListSessionsSql,
            transaction: transaction);
        var summaries = new List<StoredSessionSummary>();
        foreach (var row in rows)
        {
            var session = MapSession(row);
            if (!session.IsSuccess ||
                row.IncidentCount is < 0 or > int.MaxValue ||
                row.PendingIncidentCount is < 0 or > int.MaxValue ||
                row.PendingIncidentCount > row.IncidentCount)
            {
                return Result<IReadOnlyList<StoredSessionSummary>>.Failure(StoreErrors.PersistenceFailure);
            }

            summaries.Add(StoredSessionSummary.Create(
                session.Value,
                (int)row.IncidentCount,
                (int)row.PendingIncidentCount));
        }

        return Result<IReadOnlyList<StoredSessionSummary>>.Success(summaries.AsReadOnly());
    }

    private static Result<StoreLookup<StoredSessionDetails>> QuerySessionDetails(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionIdentity session,
        CancellationToken cancellationToken)
    {
        var sessionResult = QuerySession(connection, transaction, session);
        if (!sessionResult.IsSuccess)
        {
            return Result<StoreLookup<StoredSessionDetails>>.Failure(sessionResult.Error!);
        }

        if (!sessionResult.Value.IsFound)
        {
            return Result<StoreLookup<StoredSessionDetails>>.Success(
                StoreLookup.Missing<StoredSessionDetails>());
        }

        cancellationToken.ThrowIfCancellationRequested();
        var incidents = QueryIncidents(connection, transaction, session);
        if (!incidents.IsSuccess)
        {
            return Result<StoreLookup<StoredSessionDetails>>.Failure(incidents.Error!);
        }

        return Result<StoreLookup<StoredSessionDetails>>.Success(
            StoreLookup.Found(StoredSessionDetails.Create(
                sessionResult.Value.Value!,
                incidents.Value)));
    }

    private static Result<StoreLookup<StoredIncident>> QueryIncident(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IncidentId incident)
    {
        var row = connection.QuerySingleOrDefault<IncidentRow>(
            SelectIncidentByIdSql,
            new { IncidentId = incident.ToString() },
            transaction: transaction);
        if (row is null)
        {
            return Result<StoreLookup<StoredIncident>>.Success(StoreLookup.Missing<StoredIncident>());
        }

        var mapped = MapIncident(row);
        return mapped.IsSuccess
            ? Result<StoreLookup<StoredIncident>>.Success(StoreLookup.Found(mapped.Value))
            : Result<StoreLookup<StoredIncident>>.Failure(mapped.Error!);
    }

    private static Result<IReadOnlyList<StoredIncident>> QueryIncidents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionIdentity session)
    {
        var rows = connection.Query<IncidentRow>(
            SelectIncidentsSql,
            new { SessionId = session.ToString() },
            transaction: transaction);
        var incidents = new List<StoredIncident>();
        foreach (var row in rows)
        {
            var mapped = MapIncident(row);
            if (!mapped.IsSuccess)
            {
                return Result<IReadOnlyList<StoredIncident>>.Failure(mapped.Error!);
            }

            incidents.Add(mapped.Value);
        }

        return Result<IReadOnlyList<StoredIncident>>.Success(incidents.AsReadOnly());
    }

    private static Result<StoreLookup<IncidentCheckpoint>> QueryCheckpoint(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionIdentity session)
    {
        var row = connection.QuerySingleOrDefault<IncidentCheckpointRow>(
            SelectCheckpointSql,
            new { SessionId = session.ToString() },
            transaction: transaction);
        if (row is null)
        {
            return Result<StoreLookup<IncidentCheckpoint>>.Success(
                StoreLookup.Missing<IncidentCheckpoint>());
        }

        var mapped = MapCheckpoint(row);
        return mapped.IsSuccess
            ? Result<StoreLookup<IncidentCheckpoint>>.Success(StoreLookup.Found(mapped.Value))
            : Result<StoreLookup<IncidentCheckpoint>>.Failure(mapped.Error!);
    }

    private Result WriteApplicationCommand(IStoreCommand command, CancellationToken cancellationToken)
    {
        var descriptor = ApplicationCommandFingerprint.Describe(command);
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
                        descriptor.Kind,
                        descriptor.Version,
                        descriptor.Fingerprint)
                        ? Result.Success()
                        : Result.Failure(StoreErrors.OperationIdConflict);
                }

                var handlerResult = command switch
                {
                    EnsureSession ensure => EnsureSessionCore(
                        connection,
                        transaction,
                        ensure,
                        cancellationToken),
                    EstablishIncidentCheckpoint establish => EstablishCheckpointCore(
                        connection,
                        transaction,
                        establish,
                        cancellationToken),
                    RecordDetectedIncident record => RecordIncidentCore(
                        connection,
                        transaction,
                        record,
                        cancellationToken),
                    AnnotateIncident annotate => AnnotateIncidentCore(
                        connection,
                        transaction,
                        annotate,
                        cancellationToken),
                    MarkIncidentReviewed reviewed => MarkIncidentReviewedCore(
                        connection,
                        transaction,
                        reviewed,
                        cancellationToken),
                    _ => Result.Failure(StoreErrors.UnsupportedRequest),
                };
                cancellationToken.ThrowIfCancellationRequested();
                if (!handlerResult.IsSuccess)
                {
                    return handlerResult;
                }

                var operationInserted = connection.Execute(
                    InsertOperationSql,
                    new
                    {
                        OperationId = command.OperationId.ToString(),
                        CommandKind = descriptor.Kind,
                        CommandVersion = descriptor.Version,
                        PayloadFingerprint = descriptor.Fingerprint,
                        CommittedAtUnixMilliseconds = GetCommandTimestamp(command),
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

    private static Result EnsureSessionCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EnsureSession command,
        CancellationToken cancellationToken)
    {
        var identityExists = connection.QuerySingle<long>(
            CreateDapperCommand(
                "SELECT EXISTS (SELECT 1 FROM \"Session\" WHERE session_id = @SessionId);",
                new { SessionId = command.ProposedIdentity.ToString() },
                transaction,
                cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (identityExists == 1)
        {
            return Result.Failure(StoreErrors.SessionIdentityConflict);
        }

        if (command.Descriptor.IdentityScope == SimulatorIdentityScope.Durable)
        {
            var keyExists = connection.QuerySingle<long>(
                CreateDapperCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM "Session"
                        WHERE simulator = @Simulator
                          AND simulator_session_key = @SessionKey
                          AND identity_kind = 1);
                    """,
                    new
                    {
                        Simulator = command.Descriptor.Simulator.Value,
                        SessionKey = command.Descriptor.SessionKey.Value,
                    },
                    transaction,
                    cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (keyExists == 1)
            {
                return Result.Failure(StoreErrors.SessionIdentityConflict);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var changed = connection.Execute(
            """
            INSERT INTO "Session" (
                session_id, simulator, simulator_session_key, identity_kind,
                simulator_session_number, session_mode, started_at_utc_ms,
                ended_at_utc_ms, track_id, track_name, car_id, car_name,
                created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                @SessionId, @Simulator, @SessionKey, @IdentityKind,
                @SessionNumber, @SessionMode, @StartedAt,
                NULL, NULL, NULL, NULL, NULL, @StartedAt, @StartedAt);
            """,
            new
            {
                SessionId = command.ProposedIdentity.ToString(),
                Simulator = command.Descriptor.Simulator.Value,
                SessionKey = command.Descriptor.SessionKey.Value,
                IdentityKind = command.Descriptor.IdentityScope.Value,
                SessionNumber = command.Descriptor.SessionNumber.Value,
                SessionMode = command.Descriptor.Mode.Value,
                StartedAt = command.StartedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1 ? Result.Success() : Result.Failure(StoreErrors.PersistenceFailure);
    }

    private static Result EstablishCheckpointCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EstablishIncidentCheckpoint command,
        CancellationToken cancellationToken)
    {
        var current = connection.QuerySingleOrDefault<IncidentCheckpointRow>(
            CreateDapperCommand(
                SelectCheckpointSql,
                new { SessionId = command.NextCheckpoint.Session.ToString() },
                transaction,
                cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (!CheckpointMatches(current, command.ExpectedCheckpoint))
        {
            return Result.Failure(StoreErrors.CheckpointConflict);
        }

        var next = command.NextCheckpoint;
        cancellationToken.ThrowIfCancellationRequested();
        var changed = current is null
            ? connection.Execute(
                """
                INSERT INTO IncidentCheckpoint (
                    session_id, counter_epoch, last_incident_points_total,
                    last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
                VALUES (@SessionId, @CounterEpoch, @LastCounter, @SessionNumber, @SessionTime, @UpdatedAt);
                """,
                CheckpointParameters(next),
                transaction: transaction)
            : connection.Execute(
                """
                UPDATE IncidentCheckpoint
                SET counter_epoch = @CounterEpoch,
                    last_incident_points_total = @LastCounter,
                    last_replay_session_number = @SessionNumber,
                    last_replay_session_time_ms = @SessionTime,
                    updated_at_utc_ms = @UpdatedAt
                WHERE session_id = @SessionId;
                """,
                CheckpointParameters(next),
                transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1 ? Result.Success() : Result.Failure(StoreErrors.PersistenceFailure);
    }

    private Result RecordIncidentCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordDetectedIncident command,
        CancellationToken cancellationToken)
    {
        var current = connection.QuerySingleOrDefault<IncidentCheckpointRow>(
            CreateDapperCommand(
                SelectCheckpointSql,
                new { SessionId = command.ExpectedCheckpoint.Session.ToString() },
                transaction,
                cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (!CheckpointMatches(current, command.ExpectedCheckpoint))
        {
            return Result.Failure(StoreErrors.CheckpointConflict);
        }

        var incident = command.Incident;
        cancellationToken.ThrowIfCancellationRequested();
        var inserted = connection.Execute(
            """
            INSERT INTO Incident (
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status,
                classification, notes, created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                @IncidentId, @SessionId, @ReplaySessionNumber, @ReplaySessionTime,
                @ObservedAt, @PointsDelta, @PointsTotal, @CounterEpoch, @Lap,
                @LapDistance, @ReviewStatus, @Classification, @Notes, @CreatedAt, @UpdatedAt);
            """,
            new
            {
                IncidentId = incident.Id.ToString(),
                SessionId = incident.Session.ToString(),
                ReplaySessionNumber = incident.Position.SessionNumber.Value,
                ReplaySessionTime = incident.Position.SessionTime.Milliseconds,
                ObservedAt = incident.ObservedAt.UnixMilliseconds,
                PointsDelta = incident.Points.Delta,
                PointsTotal = incident.Points.Total,
                CounterEpoch = incident.CounterEpoch.Value,
                Lap = incident.Lap?.Value,
                LapDistance = incident.LapDistance?.Value,
                ReviewStatus = incident.ReviewStatus.Value,
                Classification = incident.Annotation.Classification?.Value,
                incident.Annotation.Notes,
                CreatedAt = incident.CreatedAt.UnixMilliseconds,
                UpdatedAt = incident.UpdatedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        if (inserted != 1)
        {
            return Result.Failure(StoreErrors.PersistenceFailure);
        }

        _executionCheckpoint.AfterIncidentInsert();
        cancellationToken.ThrowIfCancellationRequested();
        var updated = connection.Execute(
            """
            UPDATE IncidentCheckpoint
            SET counter_epoch = @CounterEpoch,
                last_incident_points_total = @LastCounter,
                last_replay_session_number = @SessionNumber,
                last_replay_session_time_ms = @SessionTime,
                updated_at_utc_ms = @UpdatedAt
            WHERE session_id = @SessionId
              AND counter_epoch = @ExpectedCounterEpoch
              AND last_incident_points_total = @ExpectedCounter;
            """,
            new
            {
                SessionId = command.NextCheckpoint.Session.ToString(),
                CounterEpoch = command.NextCheckpoint.CounterEpoch.Value,
                LastCounter = command.NextCheckpoint.LastCounter.Value,
                SessionNumber = command.NextCheckpoint.LastPosition.SessionNumber.Value,
                SessionTime = command.NextCheckpoint.LastPosition.SessionTime.Milliseconds,
                UpdatedAt = command.NextCheckpoint.UpdatedAt.UnixMilliseconds,
                ExpectedCounterEpoch = command.ExpectedCheckpoint.CounterEpoch.Value,
                ExpectedCounter = command.ExpectedCheckpoint.LastCounter.Value,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return updated == 1 ? Result.Success() : Result.Failure(StoreErrors.CheckpointConflict);
    }

    private static Result AnnotateIncidentCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AnnotateIncident command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = connection.Execute(
            """
            UPDATE Incident
            SET classification = @Classification,
                notes = @Notes,
                updated_at_utc_ms = MAX(updated_at_utc_ms, @UpdatedAt)
            WHERE incident_id = @IncidentId;
            """,
            new
            {
                IncidentId = command.Incident.ToString(),
                Classification = command.Annotation.Classification?.Value,
                command.Annotation.Notes,
                UpdatedAt = command.UpdatedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1 ? Result.Success() : Result.Failure(StoreErrors.EntityNotFound);
    }

    private static Result MarkIncidentReviewedCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MarkIncidentReviewed command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = connection.Execute(
            """
            UPDATE Incident
            SET review_status = @ReviewStatus,
                updated_at_utc_ms = MAX(updated_at_utc_ms, @ReviewedAt)
            WHERE incident_id = @IncidentId;
            """,
            new
            {
                IncidentId = command.Incident.ToString(),
                ReviewStatus = IncidentReviewStatus.Reviewed.Value,
                ReviewedAt = command.ReviewedAt.UnixMilliseconds,
            },
            transaction: transaction);
        cancellationToken.ThrowIfCancellationRequested();
        return changed == 1 ? Result.Success() : Result.Failure(StoreErrors.EntityNotFound);
    }

    private static object CheckpointParameters(IncidentCheckpoint checkpoint) => new
    {
        SessionId = checkpoint.Session.ToString(),
        CounterEpoch = checkpoint.CounterEpoch.Value,
        LastCounter = checkpoint.LastCounter.Value,
        SessionNumber = checkpoint.LastPosition.SessionNumber.Value,
        SessionTime = checkpoint.LastPosition.SessionTime.Milliseconds,
        UpdatedAt = checkpoint.UpdatedAt.UnixMilliseconds,
    };

    private static bool CheckpointMatches(
        IncidentCheckpointRow? row,
        IncidentCheckpoint? expected)
    {
        if (row is null || expected is null)
        {
            return row is null && expected is null;
        }

        var mapped = MapCheckpoint(row);
        return mapped.IsSuccess && mapped.Value == expected;
    }

    private static long GetCommandTimestamp(IStoreCommand command) => command switch
    {
        EnsureSession ensure => ensure.StartedAt.UnixMilliseconds,
        EstablishIncidentCheckpoint establish => establish.NextCheckpoint.UpdatedAt.UnixMilliseconds,
        RecordDetectedIncident record => record.Incident.UpdatedAt.UnixMilliseconds,
        AnnotateIncident annotate => annotate.UpdatedAt.UnixMilliseconds,
        MarkIncidentReviewed reviewed => reviewed.ReviewedAt.UnixMilliseconds,
        _ => throw new InvalidOperationException("The command has no application-store timestamp."),
    };

    private static Result<StoreLookup<StoredSession>> MapOptionalSession(SessionRow? row)
    {
        if (row is null)
        {
            return Result<StoreLookup<StoredSession>>.Success(StoreLookup.Missing<StoredSession>());
        }

        var mapped = MapSession(row);
        return mapped.IsSuccess
            ? Result<StoreLookup<StoredSession>>.Success(StoreLookup.Found(mapped.Value))
            : Result<StoreLookup<StoredSession>>.Failure(mapped.Error!);
    }

    private static Result<StoredSession> MapSession(SessionRow row)
    {
        var id = SessionIdentity.TryParse(row.SessionId);
        var simulator = SimulatorCode.TryCreate(row.Simulator);
        var key = SimulatorSessionKey.TryCreate(row.SimulatorSessionKey);
        var scope = SimulatorIdentityScope.TryCreate(row.IdentityKind);
        var sessionNumber = SessionNumber.TryCreate(row.SimulatorSessionNumber);
        var mode = SessionMode.TryCreate(row.SessionMode);
        var startedAt = UtcInstant.TryCreateUnixMilliseconds(row.StartedAtUnixMilliseconds);
        if (!id.IsSuccess || !simulator.IsSuccess || !key.IsSuccess || !scope.IsSuccess ||
            !sessionNumber.IsSuccess || !mode.IsSuccess || !startedAt.IsSuccess)
        {
            return Result<StoredSession>.Failure(StoreErrors.PersistenceFailure);
        }

        var descriptor = SimulatorSessionDescriptor.TryCreate(
            simulator.Value,
            key.Value,
            sessionNumber.Value,
            mode.Value,
            scope.Value);
        return descriptor.IsSuccess
            ? Result<StoredSession>.Success(StoredSession.Create(id.Value, descriptor.Value, startedAt.Value))
            : Result<StoredSession>.Failure(StoreErrors.PersistenceFailure);
    }

    private static Result<StoredIncident> MapIncident(IncidentRow row)
    {
        var id = IncidentId.TryParse(row.IncidentId);
        var session = SessionIdentity.TryParse(row.SessionId);
        var sessionNumber = SessionNumber.TryCreate(row.ReplaySessionNumber);
        var sessionTime = SessionTime.TryCreateMilliseconds(row.ReplaySessionTimeMilliseconds);
        var observedAt = UtcInstant.TryCreateUnixMilliseconds(row.ObservedAtUnixMilliseconds);
        var points = IncidentPoints.TryCreate(row.IncidentPointsTotal, row.IncidentPointsDelta);
        var epoch = CounterEpoch.TryCreate(row.CounterEpoch);
        var status = IncidentReviewStatus.TryCreate(row.ReviewStatus);
        var createdAt = UtcInstant.TryCreateUnixMilliseconds(row.CreatedAtUnixMilliseconds);
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(row.UpdatedAtUnixMilliseconds);
        var lap = row.Lap.HasValue ? LapNumber.TryCreate(row.Lap.Value) : null;
        var distance = row.LapDistance.HasValue ? LapDistance.TryCreate(row.LapDistance.Value) : null;
        var classification = row.Classification.HasValue
            ? IncidentClassification.TryCreate(row.Classification.Value)
            : null;
        if (!id.IsSuccess || !session.IsSuccess || !sessionNumber.IsSuccess || !sessionTime.IsSuccess ||
            !observedAt.IsSuccess || !points.IsSuccess || !epoch.IsSuccess || !status.IsSuccess ||
            !createdAt.IsSuccess || !updatedAt.IsSuccess ||
            (lap is not null && !lap.IsSuccess) ||
            (distance is not null && !distance.IsSuccess) ||
            (classification is not null && !classification.IsSuccess))
        {
            return Result<StoredIncident>.Failure(StoreErrors.PersistenceFailure);
        }

        var position = ReplayPosition.TryCreate(sessionNumber.Value, sessionTime.Value);
        var annotation = IncidentAnnotation.TryCreate(row.Notes, classification?.Value);
        if (!position.IsSuccess || !annotation.IsSuccess ||
            updatedAt.Value.UnixMilliseconds < createdAt.Value.UnixMilliseconds)
        {
            return Result<StoredIncident>.Failure(StoreErrors.PersistenceFailure);
        }

        return Result<StoredIncident>.Success(StoredIncident.Create(
            id.Value,
            session.Value,
            position.Value,
            observedAt.Value,
            points.Value,
            epoch.Value,
            lap?.Value,
            distance?.Value,
            status.Value,
            annotation.Value,
            createdAt.Value,
            updatedAt.Value));
    }

    private static Result<IncidentCheckpoint> MapCheckpoint(IncidentCheckpointRow row)
    {
        var session = SessionIdentity.TryParse(row.SessionId);
        var epoch = CounterEpoch.TryCreate(row.CounterEpoch);
        var counter = IncidentCounter.TryCreate(row.LastIncidentPointsTotal);
        var number = SessionNumber.TryCreate(row.LastReplaySessionNumber);
        var time = SessionTime.TryCreateMilliseconds(row.LastReplaySessionTimeMilliseconds);
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(row.UpdatedAtUnixMilliseconds);
        if (!session.IsSuccess || !epoch.IsSuccess || !counter.IsSuccess || !number.IsSuccess ||
            !time.IsSuccess || !updatedAt.IsSuccess)
        {
            return Result<IncidentCheckpoint>.Failure(StoreErrors.PersistenceFailure);
        }

        var position = ReplayPosition.TryCreate(number.Value, time.Value);
        return position.IsSuccess
            ? IncidentCheckpoint.TryCreate(
                session.Value,
                epoch.Value,
                counter.Value,
                position.Value,
                updatedAt.Value)
            : Result<IncidentCheckpoint>.Failure(StoreErrors.PersistenceFailure);
    }
}
