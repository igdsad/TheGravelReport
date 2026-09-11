using System.Security.Cryptography;
using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.Sqlite.Tests;

[TestClass]
public sealed class SqliteInitializationTests
{
    private const string VersionFourUuid = "550e8400-e29b-41d4-a716-446655440000";
    private const string VersionSevenUuidWithInvalidVariant =
        "01941f29-7c00-7000-7000-000000000001";

    private static readonly string[] ExpectedTables =
    [
        "ApplicationPreferences",
        "CustomEvent",
        "Incident",
        "IncidentCheckpoint",
        "SchemaVersions",
        "Session",
        "StoreOperation",
    ];

    private static readonly string[] ExpectedSessionColumns =
    [
        "session_id", "simulator", "simulator_session_key", "identity_kind",
        "simulator_session_number", "session_mode", "started_at_utc_ms",
        "ended_at_utc_ms", "track_id", "track_name", "car_id", "car_name",
        "created_at_utc_ms", "updated_at_utc_ms",
    ];

    private static readonly string[] ExpectedPreferencesColumns =
    [
        "preferences_id", "replay_lead_in_ms", "auto_pause", "playback_speed",
        "preferred_camera", "updated_at_utc_ms", "theme_preference", "submitter_name",
        "custom_event_key", "event_join_code",
    ];

    private static readonly string[] ExpectedCustomEventColumns =
    [
        "custom_event_id", "session_id", "replay_session_number",
        "replay_session_time_ms", "submitter_name", "occurred_at_utc_ms",
        "synchronized_at_utc_ms",
    ];

    private static readonly string[] ExpectedIncidentColumns =
    [
        "incident_id", "session_id", "replay_session_number", "replay_session_time_ms",
        "observed_at_utc_ms", "incident_points_delta", "incident_points_total",
        "counter_epoch", "lap", "lap_distance_percent", "review_status",
        "classification", "notes", "created_at_utc_ms", "updated_at_utc_ms",
        "participant_identity", "driver_name", "team_name", "car_number",
    ];

    private static readonly string[] ExpectedCheckpointColumns =
    [
        "session_id", "participant_identity", "counter_epoch", "last_incident_points_total",
        "last_replay_session_number", "last_replay_session_time_ms", "updated_at_utc_ms",
    ];

    private static readonly string[] ExpectedMigrationScripts =
    [
        "001_InitialSchema.sql",
        "002_AddThemePreference.sql",
        "003_AddIncidentParticipants.sql",
        "004_DeterministicIdentitiesAndEventSettings.sql",
        "005_AddCustomEventOutbox.sql",
    ];

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task NewDatabaseAppliesExactManifestAndRequiredPragmas()
    {
        using var database = new TemporarySqliteDatabase();
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options);

        var result = await initializer.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        using var connection = database.OpenConnection();
        CollectionAssert.AreEqual(ExpectedTables, ReadTableNames(connection));
        Assert.AreEqual(5L, ExecuteScalarInt64(connection, "PRAGMA user_version;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "PRAGMA foreign_keys;"));
        Assert.AreEqual("delete", ExecuteScalarString(connection, "PRAGMA journal_mode;"));
        CollectionAssert.AreEqual(ExpectedMigrationScripts, ReadMigrationScriptNames(connection));
        CollectionAssert.AreEqual(ExpectedSessionColumns, ReadColumnNames(connection, "Session"));
        CollectionAssert.AreEqual(ExpectedIncidentColumns, ReadColumnNames(connection, "Incident"));
        CollectionAssert.AreEqual(
            ExpectedCheckpointColumns,
            ReadColumnNames(connection, "IncidentCheckpoint"));
        CollectionAssert.AreEqual(
            ExpectedPreferencesColumns,
            ReadColumnNames(connection, "ApplicationPreferences"));
        CollectionAssert.AreEqual(
            ExpectedCustomEventColumns,
            ReadColumnNames(connection, "CustomEvent"));
        Assert.AreEqual(0L, ExecuteScalarInt64(
            connection,
            "SELECT theme_preference FROM ApplicationPreferences WHERE preferences_id = 1;"));
        Assert.AreEqual("F9", ExecuteScalarString(
            connection,
            "SELECT custom_event_key FROM ApplicationPreferences WHERE preferences_id = 1;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task ReinitializationDoesNotReapplyJournaledMigration()
    {
        using var database = new TemporarySqliteDatabase();
        var first = SqliteTestingRegistration.CreateInitializer(database.Options);
        var second = SqliteTestingRegistration.CreateInitializer(database.Options);

        Assert.IsTrue((await first.InitializeAsync(CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await first.InitializeAsync(CancellationToken.None)).IsSuccess);
        Assert.IsTrue((await second.InitializeAsync(CancellationToken.None)).IsSuccess);

        using var connection = database.OpenConnection();
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '001_InitialSchema.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '002_AddThemePreference.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '003_AddIncidentParticipants.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '004_DeterministicIdentitiesAndEventSettings.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '005_AddCustomEventOutbox.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM ApplicationPreferences;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task VersionOneDatabaseUpgradesWithoutLosingReplayPreferences()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteParameterized(
                connection,
                """
                UPDATE ApplicationPreferences
                SET replay_lead_in_ms = @leadIn,
                    auto_pause = @autoPause,
                    playback_speed = @playbackSpeed,
                    preferred_camera = @camera,
                    updated_at_utc_ms = @updatedAt
                WHERE preferences_id = 1;
                """,
                ("@leadIn", 12_345),
                ("@autoPause", 0),
                ("@playbackSpeed", 0.5),
                ("@camera", "TV 2"),
                ("@updatedAt", 1_234_567));
            DowngradeToVersionOne(connection);
        }

        await using var context = SqliteTestingRegistration.CreateStore(database.Options);
        var initialization = await context.Initializer.InitializeAsync(CancellationToken.None);
        var preferences = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);

        Assert.IsTrue(initialization.IsSuccess, initialization.Error?.ToString());
        Assert.IsTrue(preferences.IsSuccess, preferences.Error?.ToString());
        Assert.AreEqual(12_345L, preferences.Value.ReplayLeadInMilliseconds);
        Assert.IsFalse(preferences.Value.AutoPause);
        Assert.AreEqual(0.5, preferences.Value.PlaybackSpeed);
        Assert.AreEqual("TV 2", preferences.Value.PreferredCamera);
        Assert.AreEqual(ThemePreference.FollowDesktop, preferences.Value.Theme);
        Assert.IsNull(preferences.Value.SubmitterName);
        Assert.AreEqual("F9", preferences.Value.CustomEventKey);
        Assert.IsNull(preferences.Value.EventJoinCode);
        using var verification = database.OpenConnection();
        Assert.AreEqual(5L, ExecuteScalarInt64(verification, "PRAGMA user_version;"));
        Assert.AreEqual(1_234_567L, ExecuteScalarInt64(
            verification,
            "SELECT updated_at_utc_ms FROM ApplicationPreferences WHERE preferences_id = 1;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '002_AddThemePreference.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '003_AddIncidentParticipants.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '004_DeterministicIdentitiesAndEventSettings.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '005_AddCustomEventOutbox.sql';"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task VersionTwoDatabaseUpgradesLegacyIncidentStateToLocalParticipant()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        var session = SessionIdentity.Generate();
        var incidentId = IncidentId.Generate();
        using (var connection = database.OpenConnection())
        {
            DowngradeToVersionTwo(connection);
            InsertSession(connection, session.ToString(), "legacy-participant-session");
            InsertIncident(connection, incidentId.ToString(), session.ToString(), total: 4, delta: 2);
            ExecuteParameterized(
                connection,
                """
                INSERT INTO IncidentCheckpoint (
                    session_id, counter_epoch, last_incident_points_total,
                    last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
                VALUES (@sessionId, 3, 4, 0, 1000, 1000);
                """,
                ("@sessionId", session.ToString()));
        }

        await using var context = SqliteTestingRegistration.CreateStore(database.Options);
        var initialization = await context.Initializer.InitializeAsync(CancellationToken.None);
        var incidents = await context.Store.QueryAsync(
            new GetIncidents(session),
            CancellationToken.None);
        var checkpoints = await context.Store.QueryAsync(
            new GetIncidentCheckpoints(session),
            CancellationToken.None);

        Assert.IsTrue(initialization.IsSuccess, initialization.Error?.ToString());
        Assert.IsTrue(incidents.IsSuccess, incidents.Error?.ToString());
        Assert.HasCount(1, incidents.Value);
        Assert.AreEqual(incidentId, incidents.Value[0].Id);
        Assert.AreEqual("local-player", incidents.Value[0].Participant.Identity.Value);
        Assert.IsNull(incidents.Value[0].Participant.DriverName);
        Assert.IsNull(incidents.Value[0].Participant.TeamName);
        Assert.IsNull(incidents.Value[0].Participant.CarNumber);
        Assert.IsTrue(checkpoints.IsSuccess, checkpoints.Error?.ToString());
        Assert.HasCount(1, checkpoints.Value);
        Assert.AreEqual("local-player", checkpoints.Value[0].ParticipantIdentity.Value);
        Assert.AreEqual(3, checkpoints.Value[0].CounterEpoch.Value);
        Assert.AreEqual(4, checkpoints.Value[0].LastCounter.Value);

        using var verification = database.OpenConnection();
        Assert.AreEqual(5L, ExecuteScalarInt64(verification, "PRAGMA user_version;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '003_AddIncidentParticipants.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '004_DeterministicIdentitiesAndEventSettings.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '005_AddCustomEventOutbox.sql';"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-005")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task VersionThreeDatabaseUpgradesWithoutLosingMultipleParticipants()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        var session = SessionIdentity.Generate();
        var firstIncident = IncidentId.Generate();
        var secondIncident = IncidentId.Generate();
        using (var connection = database.OpenConnection())
        {
            InsertSession(connection, session.ToString(), "multi-participant-upgrade");
            InsertParticipantIncident(
                connection,
                firstIncident.ToString(),
                session.ToString(),
                "cust:101",
                "Alice Driver",
                "Alpha Team",
                "12");
            InsertParticipantIncident(
                connection,
                secondIncident.ToString(),
                session.ToString(),
                "cust:202",
                "Bob Driver",
                "Beta Team",
                "34");
            ExecuteParameterized(
                connection,
                """
                INSERT INTO IncidentCheckpoint (
                    session_id, participant_identity, counter_epoch, last_incident_points_total,
                    last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
                VALUES
                    (@sessionId, 'cust:101', 1, 4, 0, 1000, 1000),
                    (@sessionId, 'cust:202', 2, 4, 0, 2000, 2000);
                """,
                ("@sessionId", session.ToString()));
            DowngradeToVersionThree(connection);
        }

        var initialization = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        Assert.IsTrue(initialization.IsSuccess, initialization.Error?.ToString());
        using var verification = database.OpenConnection();
        Assert.AreEqual(5L, ExecuteScalarInt64(verification, "PRAGMA user_version;"));
        Assert.AreEqual(2L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM Incident;"));
        Assert.AreEqual("Alice Driver|Alpha Team|12", ExecuteScalarString(
            verification,
            """
            SELECT driver_name || '|' || team_name || '|' || car_number
            FROM Incident
            WHERE participant_identity = 'cust:101';
            """));
        Assert.AreEqual("Bob Driver|Beta Team|34", ExecuteScalarString(
            verification,
            """
            SELECT driver_name || '|' || team_name || '|' || car_number
            FROM Incident
            WHERE participant_identity = 'cust:202';
            """));
        Assert.AreEqual(2L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM IncidentCheckpoint;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '004_DeterministicIdentitiesAndEventSettings.sql';"));
        Assert.AreEqual(1L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = '005_AddCustomEventOutbox.sql';"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task FailingMigrationRollsBackPendingScriptsAndJournalEntries()
    {
        using var database = new TemporarySqliteDatabase();
        var failingMigration = new SqliteTestMigration(
            "Test_999_Fail.sql",
            """
            CREATE TABLE TestShouldRollback (id INTEGER NOT NULL PRIMARY KEY);
            INSERT INTO TableThatDoesNotExist (id) VALUES (1);
            """);
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options, failingMigration);

        var result = await initializer.InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.migration-failed");
        using var connection = database.OpenConnection();
        Assert.AreEqual(0L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'Session';"));
        Assert.AreEqual(0L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'TestShouldRollback';"));

        var journalExists = ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'SchemaVersions';") == 1;
        if (journalExists)
        {
            Assert.AreEqual(0L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM SchemaVersions;"));
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task MigrationFailureReturnsSafeStructuredError()
    {
        using var database = new TemporarySqliteDatabase();
        var initializer = SqliteTestingRegistration.CreateInitializer(
            database.Options,
            new SqliteTestMigration(
                "Test_999_SecretFailure.sql",
                "INSERT INTO SecretProviderTableName (id) VALUES (1);"));

        var result = await initializer.InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.migration-failed");
        Assert.IsFalse(result.Error!.Message.Contains(database.DatabasePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Error.Message.Contains("SecretProviderTableName", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task UnsupportedSchemaVersionReturnsSchemaInvalidFailure()
    {
        using var database = new TemporarySqliteDatabase();
        var first = SqliteTestingRegistration.CreateInitializer(database.Options);
        Assert.IsTrue((await first.InitializeAsync(CancellationToken.None)).IsSuccess);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "PRAGMA user_version = 6;");
        }

        var second = SqliteTestingRegistration.CreateInitializer(database.Options);
        var result = await second.InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    [DataRow(6)]
    [DataRow(-1)]
    public async Task UnsupportedUnjournaledSchemaVersionIsRejectedWithoutMutation(int schemaVersion)
    {
        using var providerDatabase = new TemporarySqliteDatabase();
        await InitializeSuccessfully(providerDatabase);
        using var database = new TemporarySqliteDatabase();
        using (var connection = database.CreateConnection())
        {
            ExecuteNonQuery(connection, "CREATE TABLE Sentinel (value TEXT NOT NULL);");
            ExecuteNonQuery(connection, "INSERT INTO Sentinel (value) VALUES ('preserve-me');");
            SetSchemaVersion(connection, schemaVersion);
        }

        var beforeHash = SHA256.HashData(File.ReadAllBytes(database.DatabasePath));
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options);

        var result = await initializer.InitializeAsync(CancellationToken.None);

        var afterHash = SHA256.HashData(File.ReadAllBytes(database.DatabasePath));
        AssertFailure(result, "store.sqlite.schema-invalid");
        CollectionAssert.AreEqual(beforeHash, afterHash);
        using var verification = database.OpenConnection();
        Assert.AreEqual(schemaVersion, ExecuteScalarInt64(verification, "PRAGMA user_version;"));
        Assert.AreEqual("preserve-me", ExecuteScalarString(verification, "SELECT value FROM Sentinel;"));
        Assert.AreEqual(0L, ExecuteScalarInt64(
            verification,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'SchemaVersions';"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SES-002")]
    public async Task SimulatorSessionKeyIsUniqueOnlyForDurableIdentityEvidence()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();

        InsertSession(
            connection,
            SessionIdentity.Generate().ToString(),
            "shared-key",
            identityKind: 2);
        InsertSession(
            connection,
            SessionIdentity.Generate().ToString(),
            "shared-key",
            identityKind: 2);
        InsertSession(
            connection,
            SessionIdentity.Generate().ToString(),
            "shared-key",
            identityKind: 1);

        Assert.ThrowsExactly<SqliteException>(() => InsertSession(
            connection,
            SessionIdentity.Generate().ToString(),
            "shared-key",
            identityKind: 1));
        Assert.AreEqual(3L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM \"Session\";"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsRequiredIndexWithWrongColumnOrder()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "DROP INDEX ux_session_simulator_key;");
            ExecuteNonQuery(
                connection,
                """
                CREATE UNIQUE INDEX ux_session_simulator_key
                    ON "Session" (simulator_session_key, simulator)
                    WHERE simulator_session_key IS NOT NULL AND identity_kind = 1;
                """);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsRequiredIndexWithNonCanonicalKeySemantics()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "DROP INDEX ux_session_simulator_key;");
            ExecuteNonQuery(
                connection,
                """
                CREATE UNIQUE INDEX ux_session_simulator_key
                    ON "Session" (simulator COLLATE NOCASE DESC, simulator_session_key)
                    WHERE simulator_session_key IS NOT NULL AND identity_kind = 1;
                """);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsRequiredIndexWithWrongPredicate()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "DROP INDEX ux_session_simulator_key;");
            ExecuteNonQuery(
                connection,
                """
                CREATE UNIQUE INDEX ux_session_simulator_key
                    ON "Session" (simulator, simulator_session_key)
                    WHERE simulator_session_key IS NOT NULL;
                """);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsRequiredIndexThatIsNotUnique()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "DROP INDEX ux_incident_session_participant_epoch_total;");
            ExecuteNonQuery(
                connection,
                """
                CREATE INDEX ux_incident_session_participant_epoch_total
                    ON Incident (
                        session_id, participant_identity, counter_epoch, incident_points_total);
                """);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsCheckpointWithoutParticipantInPrimaryKey()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(
                connection,
                """
                DROP TABLE IncidentCheckpoint;
                CREATE TABLE IncidentCheckpoint (
                    session_id TEXT NOT NULL CONSTRAINT pk_incident_checkpoint PRIMARY KEY,
                    participant_identity TEXT NOT NULL,
                    counter_epoch INTEGER NOT NULL,
                    last_incident_points_total INTEGER NOT NULL,
                    last_replay_session_number INTEGER NOT NULL,
                    last_replay_session_time_ms INTEGER NOT NULL,
                    updated_at_utc_ms INTEGER NOT NULL,
                    CONSTRAINT fk_checkpoint_session FOREIGN KEY (session_id)
                        REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
                    CONSTRAINT ck_checkpoint_participant_identity CHECK (
                        length(participant_identity) BETWEEN 1 AND 128
                        AND length(trim(participant_identity)) > 0),
                    CONSTRAINT ck_checkpoint_values CHECK (
                        counter_epoch BETWEEN 0 AND 2147483647
                        AND last_incident_points_total BETWEEN 0 AND 2147483647
                        AND last_replay_session_number BETWEEN 0 AND 2147483647
                        AND last_replay_session_time_ms BETWEEN 0 AND 922337203685477
                        AND updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
                );
                """);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task CancellationAfterBlockedMigrationKeepsStoreGateClosed()
    {
        using var database = new TemporarySqliteDatabase();
        using var migrationGate = new SqliteTestMigrationGate();
        using var cancellation = new CancellationTokenSource();
        await using var context = SqliteMigrationTestingRegistration.CreateStore(
            database.Options,
            migrationGate);
        var initialization = Task.Run(() => context.Initializer.InitializeAsync(cancellation.Token));

        var enteredMigration = migrationGate.WaitUntilEntered(TimeSpan.FromSeconds(10));
        if (enteredMigration)
        {
            cancellation.Cancel();
        }

        migrationGate.Release();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => initialization);

        Assert.IsTrue(enteredMigration, "The migration did not reach its deterministic test gate.");
        using var connection = database.OpenConnection();
        Assert.AreEqual(5L, ExecuteScalarInt64(connection, "PRAGMA user_version;"));
        Assert.AreEqual(6L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM SchemaVersions;"));

        var whileCanceled = await context.Store.QueryAsync(
            GetPreferences.Instance,
            CancellationToken.None);
        Assert.IsFalse(whileCanceled.IsSuccess);
        Assert.IsNotNull(whileCanceled.Error);
        Assert.AreEqual(StoreErrorCodes.NotInitialized, whileCanceled.Error.Code);

        var retry = await context.Initializer.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(retry.IsSuccess, retry.Error?.ToString());
        Assert.IsTrue((await context.Store.QueryAsync(
            GetPreferences.Instance,
            CancellationToken.None)).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    [DataRow(VersionFourUuid)]
    [DataRow(VersionSevenUuidWithInvalidVariant)]
    public async Task SchemaRejectsUnsupportedUuidVersionOrInvalidVariantIdentifiers(string invalidIdentifier)
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var validSessionId = SessionIdentity.Generate().ToString();
        InsertSession(connection, validSessionId, "valid-session");

        Assert.ThrowsExactly<SqliteException>(() =>
            InsertSession(connection, invalidIdentifier, "invalid-session"));
        Assert.ThrowsExactly<SqliteException>(() =>
            InsertIncident(connection, invalidIdentifier, validSessionId, total: 1, delta: 1));
        Assert.ThrowsExactly<SqliteException>(() =>
            InsertStoreOperation(connection, invalidIdentifier));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task SessionDeleteCascadesToIncidentAndCheckpoint()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var sessionId = SessionIdentity.Generate().ToString();
        InsertSession(connection, sessionId, "durable-key");
        InsertIncident(connection, IncidentId.Generate().ToString(), sessionId, total: 2, delta: 2);
        ExecuteParameterized(
            connection,
            """
            INSERT INTO IncidentCheckpoint (
                session_id, participant_identity, counter_epoch, last_incident_points_total,
                last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
            VALUES (@sessionId, 'participant-1', 0, 2, 0, 1000, 1000);
            """,
            ("@sessionId", sessionId));

        ExecuteParameterized(connection, "DELETE FROM \"Session\" WHERE session_id = @sessionId;", ("@sessionId", sessionId));

        Assert.AreEqual(0L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM Incident;"));
        Assert.AreEqual(0L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM IncidentCheckpoint;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task SchemaValidationRejectsExistingForeignKeyViolations()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "PRAGMA foreign_keys = OFF;");
            InsertIncident(
                connection,
                IncidentId.Generate().ToString(),
                SessionIdentity.Generate().ToString(),
                total: 2,
                delta: 2);
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsMissingPreferencesSingleton()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "DELETE FROM ApplicationPreferences WHERE preferences_id = 1;");
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsPreferencesThatCannotBeDecodedByTheDomain()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteParameterized(
                connection,
                "UPDATE ApplicationPreferences SET preferred_camera = @camera WHERE preferences_id = 1;",
                ("@camera", "Cockpit\nCamera"));
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaValidationRejectsAnInvalidPersistedTheme()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using (var connection = database.OpenConnection())
        {
            ExecuteNonQuery(connection, "PRAGMA ignore_check_constraints = ON;");
            ExecuteNonQuery(
                connection,
                "UPDATE ApplicationPreferences SET theme_preference = 99 WHERE preferences_id = 1;");
        }

        var result = await SqliteTestingRegistration.CreateInitializer(database.Options)
            .InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-004")]
    public async Task IncidentLogicalIdentityIsUniqueWithinSessionEpoch()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var sessionId = SessionIdentity.Generate().ToString();
        InsertSession(connection, sessionId, "unique-key");
        InsertIncident(connection, IncidentId.Generate().ToString(), sessionId, total: 4, delta: 4);

        Assert.ThrowsExactly<SqliteException>(() =>
            InsertIncident(connection, IncidentId.Generate().ToString(), sessionId, total: 4, delta: 1));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-002")]
    public async Task IncidentChecksRejectNonPositiveDeltaAndInvalidPercentage()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var sessionId = SessionIdentity.Generate().ToString();
        InsertSession(connection, sessionId, "checks-key");

        Assert.ThrowsExactly<SqliteException>(() =>
            InsertIncident(connection, IncidentId.Generate().ToString(), sessionId, total: 1, delta: 0));
        Assert.ThrowsExactly<SqliteException>(() =>
            InsertIncident(
                connection,
                IncidentId.Generate().ToString(),
                sessionId,
                total: 1,
                delta: 1,
                lapDistance: 1.1));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "IR-SET-002")]
    public async Task PreferencesEnforceSingletonBooleanPlaybackAndThemeValues()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();

        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM ApplicationPreferences;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            """
            INSERT INTO ApplicationPreferences
                (preferences_id, replay_lead_in_ms, auto_pause, playback_speed, updated_at_utc_ms)
            VALUES (2, 5000, 1, 1.0, 0);
            """));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET auto_pause = 3 WHERE preferences_id = 1;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET playback_speed = 3.0 WHERE preferences_id = 1;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET theme_preference = -1 WHERE preferences_id = 1;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET theme_preference = 3 WHERE preferences_id = 1;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaAcceptsFinalizedDomainCodesAndBoundaryValues()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var sessionId = SessionIdentity.Generate().ToString();
        var incidentId = IncidentId.Generate().ToString();

        ExecuteParameterized(
            connection,
            """
            INSERT INTO "Session" (
                session_id, simulator, simulator_session_key, identity_kind,
                simulator_session_number, session_mode, started_at_utc_ms, ended_at_utc_ms,
                created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                @sessionId, 'iracing', 'boundary-key', 2,
                2147483647, 2, -62135596800000, 253402300799999,
                -62135596800000, 253402300799999);
            """,
            ("@sessionId", sessionId));
        ExecuteParameterized(
            connection,
            """
            INSERT INTO Incident (
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status, classification, notes,
                created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                @incidentId, @sessionId, 2147483647, 922337203685477,
                -62135596800000, 1, 2147483647,
                2147483647, 2147483647, 1.0, 3, 5, @notes,
                -62135596800000, 253402300799999);
            """,
            ("@incidentId", incidentId),
            ("@sessionId", sessionId),
            ("@notes", new string('n', 2000)));
        ExecuteParameterized(
            connection,
            """
            UPDATE ApplicationPreferences
            SET replay_lead_in_ms = 60000,
                playback_speed = 0.1,
                preferred_camera = @camera,
                theme_preference = 2,
                updated_at_utc_ms = 253402300799999
            WHERE preferences_id = 1;
            """,
            ("@camera", new string('c', 128)));
        InsertStoreOperation(connection, OperationId.Create().ToString());

        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM Incident;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-002")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task SchemaRejectsValuesOutsideFinalizedDomainSets()
    {
        using var database = new TemporarySqliteDatabase();
        await InitializeSuccessfully(database);
        using var connection = database.OpenConnection();
        var sessionId = SessionIdentity.Generate().ToString();
        InsertSession(connection, sessionId, "invalid-domain-key");
        InsertIncident(connection, IncidentId.Generate().ToString(), sessionId, total: 1, delta: 1);

        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE \"Session\" SET identity_kind = 0 WHERE session_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE \"Session\" SET session_mode = 0 WHERE session_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteParameterized(
            connection,
            "UPDATE \"Session\" SET simulator_session_key = @key WHERE session_id IS NOT NULL;",
            ("@key", new string(' ', 256) + "x")));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE \"Session\" SET simulator = 'IRacing' WHERE session_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE Incident SET review_status = 0 WHERE incident_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE Incident SET classification = 6 WHERE incident_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteParameterized(
            connection,
            "UPDATE Incident SET notes = @notes WHERE incident_id IS NOT NULL;",
            ("@notes", new string('n', 2001))));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET replay_lead_in_ms = 60001 WHERE preferences_id = 1;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteParameterized(
            connection,
            "UPDATE ApplicationPreferences SET preferred_camera = @camera WHERE preferences_id = 1;",
            ("@camera", new string(' ', 128) + "c")));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE ApplicationPreferences SET theme_preference = 3 WHERE preferences_id = 1;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE \"Session\" SET started_at_utc_ms = -62135596800001 WHERE session_id IS NOT NULL;"));
        Assert.ThrowsExactly<SqliteException>(() => ExecuteNonQuery(
            connection,
            "UPDATE Incident SET observed_at_utc_ms = 253402300800000 WHERE incident_id IS NOT NULL;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task CancellationBeforeInitializationPropagatesAndCreatesNoDatabase()
    {
        using var database = new TemporarySqliteDatabase();
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            initializer.InitializeAsync(cancellation.Token));
        Assert.IsFalse(File.Exists(database.DatabasePath));
    }

    private static void DowngradeToVersionOne(SqliteConnection connection)
    {
        DowngradeToVersionTwo(connection);
        ExecuteNonQuery(connection, "ALTER TABLE ApplicationPreferences DROP COLUMN theme_preference;");
        ExecuteNonQuery(
            connection,
            "DELETE FROM SchemaVersions WHERE ScriptName = '002_AddThemePreference.sql';");
        ExecuteNonQuery(connection, "PRAGMA user_version = 1;");
    }

    private static void DowngradeToVersionTwo(SqliteConnection connection)
    {
        DowngradeToVersionThree(connection);
        ExecuteNonQuery(connection, "DROP INDEX ux_incident_session_participant_epoch_total;");
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IncidentV2 (
                incident_id TEXT NOT NULL CONSTRAINT pk_incident PRIMARY KEY,
                session_id TEXT NOT NULL,
                replay_session_number INTEGER NOT NULL,
                replay_session_time_ms INTEGER NOT NULL,
                observed_at_utc_ms INTEGER NOT NULL,
                incident_points_delta INTEGER NOT NULL,
                incident_points_total INTEGER NOT NULL,
                counter_epoch INTEGER NOT NULL,
                lap INTEGER NULL,
                lap_distance_percent REAL NULL,
                review_status INTEGER NOT NULL,
                classification INTEGER NULL,
                notes TEXT NULL,
                created_at_utc_ms INTEGER NOT NULL,
                updated_at_utc_ms INTEGER NOT NULL,
                CONSTRAINT fk_incident_session FOREIGN KEY (session_id)
                    REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
                CONSTRAINT ck_incident_id_uuid CHECK (
                    length(incident_id) = 36
                    AND substr(incident_id, 9, 1) = '-'
                    AND substr(incident_id, 14, 1) = '-'
                    AND substr(incident_id, 19, 1) = '-'
                    AND substr(incident_id, 24, 1) = '-'
                    AND substr(incident_id, 15, 1) = '7'
                    AND substr(incident_id, 20, 1) IN ('8', '9', 'a', 'b')
                    AND incident_id = lower(incident_id)
                    AND length(replace(incident_id, '-', '')) = 32
                    AND replace(incident_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
                CONSTRAINT ck_incident_replay_position CHECK (
                    replay_session_number BETWEEN 0 AND 2147483647
                    AND replay_session_time_ms BETWEEN 0 AND 922337203685477),
                CONSTRAINT ck_incident_observed_time CHECK (
                    observed_at_utc_ms BETWEEN -62135596800000 AND 253402300799999),
                CONSTRAINT ck_incident_points CHECK (
                    incident_points_delta BETWEEN 1 AND 2147483647
                    AND incident_points_total BETWEEN incident_points_delta AND 2147483647
                    AND counter_epoch BETWEEN 0 AND 2147483647),
                CONSTRAINT ck_incident_lap CHECK (
                    lap IS NULL OR lap BETWEEN 0 AND 2147483647),
                CONSTRAINT ck_incident_lap_distance CHECK (
                    lap_distance_percent IS NULL
                    OR (lap_distance_percent >= 0.0 AND lap_distance_percent <= 1.0)),
                CONSTRAINT ck_incident_review_status CHECK (review_status IN (1, 2, 3)),
                CONSTRAINT ck_incident_classification CHECK (
                    classification IS NULL OR classification IN (1, 2, 3, 4, 5)),
                CONSTRAINT ck_incident_notes CHECK (notes IS NULL OR length(notes) <= 2000),
                CONSTRAINT ck_incident_audit_times CHECK (
                    created_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
                    AND updated_at_utc_ms BETWEEN created_at_utc_ms AND 253402300799999)
            );

            INSERT INTO IncidentV2 (
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status, classification,
                notes, created_at_utc_ms, updated_at_utc_ms)
            SELECT
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status, classification,
                notes, created_at_utc_ms, updated_at_utc_ms
            FROM Incident;

            DROP TABLE Incident;
            ALTER TABLE IncidentV2 RENAME TO Incident;

            CREATE UNIQUE INDEX ux_incident_session_epoch_total
                ON Incident (session_id, counter_epoch, incident_points_total);
            """);
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IncidentCheckpointV2 (
                session_id TEXT NOT NULL CONSTRAINT pk_incident_checkpoint PRIMARY KEY,
                counter_epoch INTEGER NOT NULL,
                last_incident_points_total INTEGER NOT NULL,
                last_replay_session_number INTEGER NOT NULL,
                last_replay_session_time_ms INTEGER NOT NULL,
                updated_at_utc_ms INTEGER NOT NULL,
                CONSTRAINT fk_checkpoint_session FOREIGN KEY (session_id)
                    REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
                CONSTRAINT ck_checkpoint_values CHECK (
                    counter_epoch BETWEEN 0 AND 2147483647
                    AND last_incident_points_total BETWEEN 0 AND 2147483647
                    AND last_replay_session_number BETWEEN 0 AND 2147483647
                    AND last_replay_session_time_ms BETWEEN 0 AND 922337203685477
                    AND updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
            );

            INSERT INTO IncidentCheckpointV2 (
                session_id, counter_epoch, last_incident_points_total,
                last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
            SELECT
                session_id, counter_epoch, last_incident_points_total,
                last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms
            FROM IncidentCheckpoint;

            DROP TABLE IncidentCheckpoint;
            ALTER TABLE IncidentCheckpointV2 RENAME TO IncidentCheckpoint;
            """);
        ExecuteNonQuery(
            connection,
            "DELETE FROM SchemaVersions WHERE ScriptName = '003_AddIncidentParticipants.sql';");
        ExecuteNonQuery(connection, "PRAGMA user_version = 2;");
    }

    private static void DowngradeToVersionThree(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "DROP TABLE CustomEvent;");
        ExecuteNonQuery(connection, "ALTER TABLE ApplicationPreferences DROP COLUMN submitter_name;");
        ExecuteNonQuery(connection, "ALTER TABLE ApplicationPreferences DROP COLUMN custom_event_key;");
        ExecuteNonQuery(connection, "ALTER TABLE ApplicationPreferences DROP COLUMN event_join_code;");
        ExecuteNonQuery(
            connection,
            "DELETE FROM SchemaVersions WHERE ScriptName = '004_DeterministicIdentitiesAndEventSettings.sql';");
        ExecuteNonQuery(
            connection,
            "DELETE FROM SchemaVersions WHERE ScriptName = '005_AddCustomEventOutbox.sql';");
        ExecuteNonQuery(connection, "PRAGMA user_version = 3;");
    }

    private static async Task InitializeSuccessfully(TemporarySqliteDatabase database)
    {
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options);
        var result = await initializer.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
    }

    private static string[] ReadTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static string[] ReadColumnNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "Session" => "PRAGMA table_info('Session');",
            "Incident" => "PRAGMA table_info('Incident');",
            "IncidentCheckpoint" => "PRAGMA table_info('IncidentCheckpoint');",
            "ApplicationPreferences" => "PRAGMA table_info('ApplicationPreferences');",
            "CustomEvent" => "PRAGMA table_info('CustomEvent');",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names.ToArray();
    }

    private static string[] ReadMigrationScriptNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static void InsertSession(
        SqliteConnection connection,
        string sessionId,
        string simulatorKey,
        int identityKind = 1)
    {
        ExecuteParameterized(
            connection,
            """
            INSERT INTO "Session" (
                session_id, simulator, simulator_session_key, identity_kind,
                simulator_session_number, session_mode,
                started_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
            VALUES (@sessionId, 'iracing', @simulatorKey, @identityKind, 0, 1, 1000, 1000, 1000);
            """,
            ("@sessionId", sessionId),
            ("@simulatorKey", simulatorKey),
            ("@identityKind", identityKind));
    }

    private static void InsertIncident(
        SqliteConnection connection,
        string incidentId,
        string sessionId,
        int total,
        int delta,
        double lapDistance = 0.5)
    {
        ExecuteParameterized(
            connection,
            """
            INSERT INTO Incident (
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status,
                created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                @incidentId, @sessionId, 0, 1000,
                1000, @delta, @total,
                0, 1, @lapDistance, 1,
                1000, 1000);
            """,
            ("@incidentId", incidentId),
            ("@sessionId", sessionId),
            ("@delta", delta),
            ("@total", total),
            ("@lapDistance", lapDistance));
    }

    private static void InsertParticipantIncident(
        SqliteConnection connection,
        string incidentId,
        string sessionId,
        string participantIdentity,
        string driverName,
        string teamName,
        string carNumber)
    {
        ExecuteParameterized(
            connection,
            """
            INSERT INTO Incident (
                incident_id, session_id, replay_session_number, replay_session_time_ms,
                observed_at_utc_ms, incident_points_delta, incident_points_total,
                counter_epoch, lap, lap_distance_percent, review_status,
                created_at_utc_ms, updated_at_utc_ms,
                participant_identity, driver_name, team_name, car_number)
            VALUES (
                @incidentId, @sessionId, 0, 1000,
                1000, 4, 4,
                0, 1, 0.5, 1,
                1000, 1000,
                @participantIdentity, @driverName, @teamName, @carNumber);
            """,
            ("@incidentId", incidentId),
            ("@sessionId", sessionId),
            ("@participantIdentity", participantIdentity),
            ("@driverName", driverName),
            ("@teamName", teamName),
            ("@carNumber", carNumber));
    }

    private static void InsertStoreOperation(SqliteConnection connection, string operationId)
    {
        ExecuteParameterized(
            connection,
            """
            INSERT INTO StoreOperation (
                operation_id, command_kind, command_version,
                payload_fingerprint_sha256, committed_at_utc_ms)
            VALUES (@operationId, 'test.operation', 1, @fingerprint, 1000);
            """,
            ("@operationId", operationId),
            ("@fingerprint", new byte[32]));
    }

    private static void SetSchemaVersion(SqliteConnection connection, int schemaVersion)
    {
        var sql = schemaVersion switch
        {
            -1 => "PRAGMA user_version = -1;",
            6 => "PRAGMA user_version = 6;",
            _ => throw new ArgumentOutOfRangeException(nameof(schemaVersion)),
        };
        ExecuteNonQuery(connection, sql);
    }

    private static void ExecuteParameterized(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            _ = command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        _ = command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    private static long ExecuteScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ExecuteScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static void AssertFailure(Result result, string errorCode)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(errorCode, result.Error.Code.ToString());
        Assert.AreEqual(ErrorKind.Persistence, result.Error.Kind);
    }
}
