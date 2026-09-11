using System.Text;
using Dapper;
using IncidentReview.Domain;
using IncidentReview.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteSchemaValidator
{
    private const int CurrentSchemaVersion = 5;
    private static readonly Version MinimumSqliteVersion = new(3, 8, 0);

    private static readonly string[] RequiredTables =
    [
        "ApplicationPreferences",
        "CustomEvent",
        "Incident",
        "IncidentCheckpoint",
        "SchemaVersions",
        "Session",
        "StoreOperation",
    ];

    private static readonly (string Table, string[] Columns)[] RequiredColumns =
    [
        ("Session",
        [
            "session_id", "simulator", "simulator_session_key", "identity_kind",
            "simulator_session_number", "session_mode", "started_at_utc_ms",
            "ended_at_utc_ms", "track_id", "track_name", "car_id", "car_name",
            "created_at_utc_ms", "updated_at_utc_ms",
        ]),
        ("Incident",
        [
            "incident_id", "session_id", "replay_session_number", "replay_session_time_ms",
            "observed_at_utc_ms", "incident_points_delta", "incident_points_total",
            "counter_epoch", "lap", "lap_distance_percent", "review_status",
            "classification", "notes", "created_at_utc_ms", "updated_at_utc_ms",
            "participant_identity", "driver_name", "team_name", "car_number",
        ]),
        ("IncidentCheckpoint",
        [
            "session_id", "participant_identity", "counter_epoch", "last_incident_points_total",
            "last_replay_session_number", "last_replay_session_time_ms", "updated_at_utc_ms",
        ]),
        ("CustomEvent",
        [
            "custom_event_id", "session_id", "replay_session_number",
            "replay_session_time_ms", "submitter_name", "occurred_at_utc_ms",
            "synchronized_at_utc_ms",
        ]),
        ("StoreOperation",
        [
            "operation_id", "command_kind", "command_version",
            "payload_fingerprint_sha256", "committed_at_utc_ms",
        ]),
        ("ApplicationPreferences",
        [
            "preferences_id", "replay_lead_in_ms", "auto_pause",
            "playback_speed", "preferred_camera", "updated_at_utc_ms",
            "theme_preference", "submitter_name", "custom_event_key", "event_join_code",
        ]),
    ];

    private static readonly RequiredIndex SessionUniqueKey = new(
        "Session",
        "ux_session_simulator_key",
        ["simulator", "simulator_session_key"],
        NormalizeSqlFragment("simulator_session_key IS NOT NULL AND identity_kind = 1"));

    private static readonly RequiredIndex IncidentUniqueKey = new(
        "Incident",
        "ux_incident_session_participant_epoch_total",
        ["session_id", "participant_identity", "counter_epoch", "incident_points_total"],
        NormalizedWherePredicate: null);

    private readonly ILogger _logger;
    private readonly int _busyTimeoutSeconds;

    public SqliteSchemaValidator(int busyTimeoutSeconds, ILogger logger)
    {
        _busyTimeoutSeconds = busyTimeoutSeconds;
        _logger = logger;
    }

    public Result ValidateMigrationPreflight(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schemaVersion = QueryInteger(connection, "PRAGMA user_version;", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (schemaVersion is >= 0 and <= CurrentSchemaVersion)
        {
            return Result.Success();
        }

        SqliteLog.SchemaInvalid(_logger, "schema version preflight", exception: null);
        return Result.Failure(SqliteStoreErrors.SchemaInvalid);
    }

    public Result Validate(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var checks = new (string Name, Func<bool> IsValid)[]
        {
            ("engine version", () => HasSupportedEngine(connection, cancellationToken)),
            ("foreign keys", () => QueryInteger(
                connection,
                "PRAGMA foreign_keys;",
                cancellationToken) == 1),
            ("busy timeout", () => connection.DefaultTimeout == _busyTimeoutSeconds),
            ("journal mode", () => string.Equals(
                QuerySingleString(connection, "PRAGMA journal_mode;", cancellationToken),
                "delete",
                StringComparison.OrdinalIgnoreCase)),
            ("synchronous mode", () => QueryInteger(
                connection,
                "PRAGMA synchronous;",
                cancellationToken) == 2),
            ("schema version", () => QueryInteger(
                connection,
                "PRAGMA user_version;",
                cancellationToken) == CurrentSchemaVersion),
            ("integrity", () => string.Equals(
                QuerySingleString(connection, "PRAGMA integrity_check;", cancellationToken),
                "ok",
                StringComparison.Ordinal)),
            ("foreign key data", () => HasNoForeignKeyViolations(
                connection,
                cancellationToken)),
            ("tables", () => HasRequiredTables(connection, cancellationToken)),
            ("columns", () => HasRequiredColumns(connection, cancellationToken)),
            ("application preferences", () => HasValidApplicationPreferences(
                connection,
                cancellationToken)),
            ("session unique key", () => HasRequiredIndex(
                connection,
                SessionUniqueKey,
                cancellationToken)),
            ("incident unique key", () => HasRequiredIndex(
                connection,
                IncidentUniqueKey,
                cancellationToken)),
            ("checkpoint primary key", () => HasRequiredPrimaryKey(
                connection,
                "IncidentCheckpoint",
                ["session_id", "participant_identity"],
                cancellationToken)),
            ("incident foreign key", () => HasCascadeForeignKey(
                connection,
                "Incident",
                cancellationToken)),
            ("checkpoint foreign key", () => HasCascadeForeignKey(
                connection,
                "IncidentCheckpoint",
                cancellationToken)),
            ("custom-event foreign key", () => HasCascadeForeignKey(
                connection,
                "CustomEvent",
                cancellationToken)),
        };

        foreach (var check in checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isValid = check.IsValid();
            cancellationToken.ThrowIfCancellationRequested();
            if (!isValid)
            {
                SqliteLog.SchemaInvalid(_logger, check.Name, exception: null);
                return Result.Failure(SqliteStoreErrors.SchemaInvalid);
            }
        }

        return Result.Success();
    }

    private bool HasSupportedEngine(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var value = QuerySingleString(connection, "SELECT sqlite_version();", cancellationToken);
        return Version.TryParse(value, out var version) && version >= MinimumSqliteVersion;
    }

    private long QueryInteger(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken) =>
        connection.QuerySingle<long>(CreateCommand(sql, cancellationToken));

    private string QuerySingleString(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken) =>
        connection.QuerySingle<string>(CreateCommand(sql, cancellationToken));

    private CommandDefinition CreateCommand(string sql, CancellationToken cancellationToken) =>
        new(
            sql,
            commandTimeout: _busyTimeoutSeconds,
            cancellationToken: cancellationToken);

    private bool HasRequiredTables(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = connection.Query<string>(CreateCommand(
            """
                SELECT name
                FROM sqlite_schema
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;
                """,
            cancellationToken)).ToArray();

        return tables.SequenceEqual(RequiredTables, StringComparer.Ordinal);
    }

    private bool HasValidApplicationPreferences(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = connection.Query<ApplicationPreferencesValidationRow>(CreateCommand(
                """
                SELECT
                    preferences_id AS PreferencesId,
                    replay_lead_in_ms AS ReplayLeadInMilliseconds,
                    auto_pause AS AutoPause,
                    playback_speed AS PlaybackSpeed,
                    preferred_camera AS PreferredCamera,
                    theme_preference AS ThemePreference,
                    submitter_name AS SubmitterName,
                    custom_event_key AS CustomEventKey,
                    event_join_code AS EventJoinCode,
                    updated_at_utc_ms AS UpdatedAtUnixMilliseconds
                FROM ApplicationPreferences
                ORDER BY preferences_id;
                """,
                cancellationToken)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.Length != 1 || rows[0].PreferencesId != 1 || rows[0].AutoPause is < 0 or > 1)
            {
                return false;
            }

            var row = rows[0];
            return UserPreferences.TryCreateMilliseconds(
                    row.ReplayLeadInMilliseconds,
                    row.PlaybackSpeed,
                    row.AutoPause == 1,
                    row.PreferredCamera,
                    (ThemePreference)row.ThemePreference,
                    row.SubmitterName,
                    row.CustomEventKey,
                    row.EventJoinCode).IsSuccess
                && UtcInstant.TryCreateUnixMilliseconds(row.UpdatedAtUnixMilliseconds).IsSuccess;
        }
        catch (Exception exception) when (
            exception is System.Data.DataException
            or InvalidCastException
            or FormatException
            or OverflowException)
        {
            return false;
        }
    }

    private bool HasRequiredIndex(
        SqliteConnection connection,
        RequiredIndex expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText = expected.Table switch
        {
            "Session" => "PRAGMA index_list('Session');",
            "Incident" => "PRAGMA index_list('Incident');",
            _ => throw new InvalidOperationException("The schema manifest contains an unknown index table."),
        };

        using var reader = command.ExecuteReader();
        var hasExpectedCharacteristics = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(reader.GetString(1), expected.Name, StringComparison.Ordinal))
            {
                hasExpectedCharacteristics = reader.GetInt64(2) == 1
                    && string.Equals(reader.GetString(3), "c", StringComparison.Ordinal)
                    && reader.GetInt64(4) == (expected.NormalizedWherePredicate is null ? 0 : 1);
                break;
            }
        }

        reader.Close();
        cancellationToken.ThrowIfCancellationRequested();
        return hasExpectedCharacteristics
            && HasRequiredIndexColumns(connection, expected, cancellationToken)
            && string.Equals(
                ReadNormalizedWherePredicate(connection, expected, cancellationToken),
                expected.NormalizedWherePredicate,
                StringComparison.Ordinal);
    }

    private bool HasRequiredIndexColumns(
        SqliteConnection connection,
        RequiredIndex expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText = expected.Name switch
        {
            "ux_session_simulator_key" => "PRAGMA index_xinfo('ux_session_simulator_key');",
            "ux_incident_session_participant_epoch_total" =>
                "PRAGMA index_xinfo('ux_incident_session_participant_epoch_total');",
            _ => throw new InvalidOperationException("The schema manifest contains an unknown index."),
        };

        using var reader = command.ExecuteReader();
        var keyColumns = new List<string>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.GetInt64(5) == 0)
            {
                continue;
            }

            if (reader.GetInt64(0) != keyColumns.Count
                || reader.GetInt64(1) < 0
                || reader.IsDBNull(2)
                || reader.GetInt64(3) != 0
                || reader.IsDBNull(4)
                || !string.Equals(reader.GetString(4), "BINARY", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            keyColumns.Add(reader.GetString(2));
        }

        return keyColumns.SequenceEqual(expected.KeyColumns, StringComparer.Ordinal);
    }

    private string? ReadNormalizedWherePredicate(
        SqliteConnection connection,
        RequiredIndex expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText =
            "SELECT sql FROM sqlite_schema WHERE type = 'index' AND tbl_name = @table AND name = @name;";
        _ = command.Parameters.AddWithValue("@table", expected.Table);
        _ = command.Parameters.AddWithValue("@name", expected.Name);

        var value = command.ExecuteScalar();
        cancellationToken.ThrowIfCancellationRequested();
        if (value is not string sql)
        {
            return null;
        }

        var whereOffset = FindSqlKeyword(sql, "WHERE");
        return whereOffset < 0
            ? null
            : NormalizeSqlFragment(sql[(whereOffset + "WHERE".Length)..]);
    }

    private static int FindSqlKeyword(string sql, string keyword)
    {
        for (var index = 0; index <= sql.Length - keyword.Length; index++)
        {
            if (!sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var startsAtBoundary = index == 0 || !IsSqlIdentifierCharacter(sql[index - 1]);
            var afterKeyword = index + keyword.Length;
            var endsAtBoundary = afterKeyword == sql.Length
                || !IsSqlIdentifierCharacter(sql[afterKeyword]);
            if (startsAtBoundary && endsAtBoundary)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSqlIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value == '_';

    private static string NormalizeSqlFragment(string fragment)
    {
        var trimmed = fragment.Trim().TrimEnd(';');
        var normalized = new StringBuilder(trimmed.Length);
        foreach (var value in trimmed)
        {
            if (!char.IsWhiteSpace(value))
            {
                _ = normalized.Append(char.ToUpperInvariant(value));
            }
        }

        return normalized.ToString();
    }

    private bool HasRequiredColumns(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var expected in RequiredColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandTimeout = _busyTimeoutSeconds;
            command.CommandText = expected.Table switch
            {
                "Session" => "PRAGMA table_info('Session');",
                "Incident" => "PRAGMA table_info('Incident');",
                "IncidentCheckpoint" => "PRAGMA table_info('IncidentCheckpoint');",
                "CustomEvent" => "PRAGMA table_info('CustomEvent');",
                "StoreOperation" => "PRAGMA table_info('StoreOperation');",
                "ApplicationPreferences" => "PRAGMA table_info('ApplicationPreferences');",
                _ => throw new InvalidOperationException("The schema manifest contains an unknown table."),
            };

            using var reader = command.ExecuteReader();
            var actual = new List<string>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                actual.Add(reader.GetString(1));
            }

            if (!actual.SequenceEqual(expected.Columns, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCascadeForeignKey(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText = table switch
        {
            "Incident" => "PRAGMA foreign_key_list('Incident');",
            "IncidentCheckpoint" => "PRAGMA foreign_key_list('IncidentCheckpoint');",
            "CustomEvent" => "PRAGMA foreign_key_list('CustomEvent');",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        using var reader = command.ExecuteReader();
        var foreignKeyCount = 0;
        var hasRequiredDefinition = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreignKeyCount++;
            hasRequiredDefinition =
                string.Equals(reader.GetString(2), "Session", StringComparison.Ordinal) &&
                string.Equals(reader.GetString(3), "session_id", StringComparison.Ordinal) &&
                string.Equals(reader.GetString(4), "session_id", StringComparison.Ordinal) &&
                string.Equals(reader.GetString(5), "RESTRICT", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reader.GetString(6), "CASCADE", StringComparison.OrdinalIgnoreCase);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return foreignKeyCount == 1 && hasRequiredDefinition;
    }

    private bool HasRequiredPrimaryKey(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> expectedColumns,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText = table switch
        {
            "IncidentCheckpoint" => "PRAGMA table_info('IncidentCheckpoint');",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        using var reader = command.ExecuteReader();
        var primaryKeyColumns = new SortedDictionary<long, string>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = reader.GetInt64(5);
            if (ordinal > 0 && !primaryKeyColumns.TryAdd(ordinal, reader.GetString(1)))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return primaryKeyColumns.Keys.SequenceEqual(
                   Enumerable.Range(1, expectedColumns.Count).Select(static value => (long)value)) &&
               primaryKeyColumns.Values.SequenceEqual(expectedColumns, StringComparer.Ordinal);
    }

    private bool HasNoForeignKeyViolations(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandTimeout = _busyTimeoutSeconds;
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        var hasViolation = reader.Read();
        cancellationToken.ThrowIfCancellationRequested();
        return !hasViolation;
    }

    private sealed record RequiredIndex(
        string Table,
        string Name,
        string[] KeyColumns,
        string? NormalizedWherePredicate);

    private sealed class ApplicationPreferencesValidationRow
    {
        public long PreferencesId { get; init; }

        public long ReplayLeadInMilliseconds { get; init; }

        public long AutoPause { get; init; }

        public double PlaybackSpeed { get; init; }

        public string? PreferredCamera { get; init; }

        public int ThemePreference { get; init; }

        public string? SubmitterName { get; init; }

        public required string CustomEventKey { get; init; }

        public string? EventJoinCode { get; init; }

        public long UpdatedAtUnixMilliseconds { get; init; }
    }
}
