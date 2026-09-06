using System.Text;
using Dapper;
using IncidentReview.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteSchemaValidator
{
    private const int CurrentSchemaVersion = 1;
    private static readonly Version MinimumSqliteVersion = new(3, 8, 0);

    private static readonly string[] RequiredTables =
    [
        "ApplicationPreferences",
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
        ]),
        ("IncidentCheckpoint",
        [
            "session_id", "counter_epoch", "last_incident_points_total",
            "last_replay_session_number", "last_replay_session_time_ms", "updated_at_utc_ms",
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
        ]),
    ];

    private static readonly RequiredIndex SessionUniqueKey = new(
        "Session",
        "ux_session_simulator_key",
        ["simulator", "simulator_session_key"],
        NormalizeSqlFragment("simulator_session_key IS NOT NULL AND identity_kind = 1"));

    private static readonly RequiredIndex IncidentUniqueKey = new(
        "Incident",
        "ux_incident_session_epoch_total",
        ["session_id", "counter_epoch", "incident_points_total"],
        NormalizedWherePredicate: null);

    private readonly ILogger _logger;
    private readonly int _busyTimeoutSeconds;

    public SqliteSchemaValidator(int busyTimeoutSeconds, ILogger logger)
    {
        _busyTimeoutSeconds = busyTimeoutSeconds;
        _logger = logger;
    }

    public Result ValidateMigrationPreflight(SqliteConnection connection)
    {
        var schemaVersion = QueryInteger(connection, "PRAGMA user_version;");
        if (schemaVersion is >= 0 and <= CurrentSchemaVersion)
        {
            return Result.Success();
        }

        SqliteLog.SchemaInvalid(_logger, "schema version preflight", exception: null);
        return Result.Failure(SqliteStoreErrors.SchemaInvalid);
    }

    public Result Validate(SqliteConnection connection)
    {
        var checks = new (string Name, Func<bool> IsValid)[]
        {
            ("engine version", () => HasSupportedEngine(connection)),
            ("foreign keys", () => QueryInteger(connection, "PRAGMA foreign_keys;") == 1),
            ("busy timeout", () => connection.DefaultTimeout == _busyTimeoutSeconds),
            ("journal mode", () => string.Equals(
                connection.QuerySingle<string>("PRAGMA journal_mode;"),
                "delete",
                StringComparison.OrdinalIgnoreCase)),
            ("synchronous mode", () => QueryInteger(connection, "PRAGMA synchronous;") == 2),
            ("schema version", () => QueryInteger(connection, "PRAGMA user_version;") == CurrentSchemaVersion),
            ("integrity", () => string.Equals(
                connection.QuerySingle<string>("PRAGMA integrity_check;"),
                "ok",
                StringComparison.Ordinal)),
            ("tables", () => HasRequiredTables(connection)),
            ("columns", () => HasRequiredColumns(connection)),
            ("session unique key", () => HasRequiredIndex(connection, SessionUniqueKey)),
            ("incident unique key", () => HasRequiredIndex(connection, IncidentUniqueKey)),
            ("incident foreign key", () => HasCascadeForeignKey(connection, "Incident")),
            ("checkpoint foreign key", () => HasCascadeForeignKey(connection, "IncidentCheckpoint")),
        };

        foreach (var check in checks)
        {
            if (!check.IsValid())
            {
                SqliteLog.SchemaInvalid(_logger, check.Name, exception: null);
                return Result.Failure(SqliteStoreErrors.SchemaInvalid);
            }
        }

        return Result.Success();
    }

    private static bool HasSupportedEngine(SqliteConnection connection)
    {
        var value = connection.QuerySingle<string>("SELECT sqlite_version();");
        return Version.TryParse(value, out var version) && version >= MinimumSqliteVersion;
    }

    private static long QueryInteger(SqliteConnection connection, string sql) =>
        connection.QuerySingle<long>(sql);

    private static bool HasRequiredTables(SqliteConnection connection)
    {
        var tables = connection.Query<string>(
            """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """).ToArray();

        return tables.SequenceEqual(RequiredTables, StringComparer.Ordinal);
    }

    private static bool HasRequiredIndex(
        SqliteConnection connection,
        RequiredIndex expected)
    {
        using var command = connection.CreateCommand();
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
            if (string.Equals(reader.GetString(1), expected.Name, StringComparison.Ordinal))
            {
                hasExpectedCharacteristics = reader.GetInt64(2) == 1
                    && string.Equals(reader.GetString(3), "c", StringComparison.Ordinal)
                    && reader.GetInt64(4) == (expected.NormalizedWherePredicate is null ? 0 : 1);
                break;
            }
        }

        reader.Close();
        return hasExpectedCharacteristics
            && HasRequiredIndexColumns(connection, expected)
            && string.Equals(
                ReadNormalizedWherePredicate(connection, expected),
                expected.NormalizedWherePredicate,
                StringComparison.Ordinal);
    }

    private static bool HasRequiredIndexColumns(
        SqliteConnection connection,
        RequiredIndex expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = expected.Name switch
        {
            "ux_session_simulator_key" => "PRAGMA index_xinfo('ux_session_simulator_key');",
            "ux_incident_session_epoch_total" =>
                "PRAGMA index_xinfo('ux_incident_session_epoch_total');",
            _ => throw new InvalidOperationException("The schema manifest contains an unknown index."),
        };

        using var reader = command.ExecuteReader();
        var keyColumns = new List<string>();
        while (reader.Read())
        {
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

    private static string? ReadNormalizedWherePredicate(
        SqliteConnection connection,
        RequiredIndex expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_schema WHERE type = 'index' AND tbl_name = @table AND name = @name;";
        _ = command.Parameters.AddWithValue("@table", expected.Table);
        _ = command.Parameters.AddWithValue("@name", expected.Name);

        if (command.ExecuteScalar() is not string sql)
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

    private static bool HasRequiredColumns(SqliteConnection connection)
    {
        foreach (var expected in RequiredColumns)
        {
            using var command = connection.CreateCommand();
            command.CommandText = expected.Table switch
            {
                "Session" => "PRAGMA table_info('Session');",
                "Incident" => "PRAGMA table_info('Incident');",
                "IncidentCheckpoint" => "PRAGMA table_info('IncidentCheckpoint');",
                "StoreOperation" => "PRAGMA table_info('StoreOperation');",
                "ApplicationPreferences" => "PRAGMA table_info('ApplicationPreferences');",
                _ => throw new InvalidOperationException("The schema manifest contains an unknown table."),
            };

            using var reader = command.ExecuteReader();
            var actual = new List<string>();
            while (reader.Read())
            {
                actual.Add(reader.GetString(1));
            }

            if (!actual.SequenceEqual(expected.Columns, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCascadeForeignKey(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "Incident" => "PRAGMA foreign_key_list('Incident');",
            "IncidentCheckpoint" => "PRAGMA foreign_key_list('IncidentCheckpoint');",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(2), "Session", StringComparison.Ordinal)
                && string.Equals(reader.GetString(3), "session_id", StringComparison.Ordinal)
                && string.Equals(reader.GetString(6), "CASCADE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record RequiredIndex(
        string Table,
        string Name,
        string[] KeyColumns,
        string? NormalizedWherePredicate);
}
