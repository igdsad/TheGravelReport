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

    private readonly ILogger _logger;
    private readonly int _busyTimeoutSeconds;

    public SqliteSchemaValidator(int busyTimeoutSeconds, ILogger logger)
    {
        _busyTimeoutSeconds = busyTimeoutSeconds;
        _logger = logger;
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
            ("session unique key", () => HasUniqueIndex(connection, "Session", "ux_session_simulator_key")),
            ("incident unique key", () => HasUniqueIndex(connection, "Incident", "ux_incident_session_epoch_total")),
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

    private static bool HasUniqueIndex(
        SqliteConnection connection,
        string table,
        string expectedIndex)
    {
        using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "Session" => "PRAGMA index_list('Session');",
            "Incident" => "PRAGMA index_list('Incident');",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt64(2) == 1
                && string.Equals(reader.GetString(1), expectedIndex, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
}
