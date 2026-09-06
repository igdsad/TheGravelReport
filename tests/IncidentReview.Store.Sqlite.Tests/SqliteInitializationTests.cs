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

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task NewDatabaseAppliesExactManifestAndRequiredPragmas()
    {
        using var database = new TemporarySqliteDatabase();
        var initializer = SqliteTestingRegistration.CreateInitializer(database.Options);

        var result = await initializer.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        using var connection = database.OpenConnection();
        CollectionAssert.AreEqual(ExpectedTables, ReadTableNames(connection));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "PRAGMA user_version;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "PRAGMA foreign_keys;"));
        Assert.AreEqual("delete", ExecuteScalarString(connection, "PRAGMA journal_mode;"));
        Assert.AreEqual("001_InitialSchema.sql", ExecuteScalarString(
            connection,
            "SELECT ScriptName FROM SchemaVersions;"));
        CollectionAssert.AreEqual(ExpectedSessionColumns, ReadColumnNames(connection, "Session"));
    }

    [TestMethod]
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
            "SELECT COUNT(*) FROM ApplicationPreferences;"));
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
            ExecuteNonQuery(connection, "PRAGMA user_version = 2;");
        }

        var second = SqliteTestingRegistration.CreateInitializer(database.Options);
        var result = await second.InitializeAsync(CancellationToken.None);

        AssertFailure(result, "store.sqlite.schema-invalid");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    [DataRow(2)]
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
            ExecuteNonQuery(connection, "DROP INDEX ux_incident_session_epoch_total;");
            ExecuteNonQuery(
                connection,
                """
                CREATE INDEX ux_incident_session_epoch_total
                    ON Incident (session_id, counter_epoch, incident_points_total);
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
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "PRAGMA user_version;"));
        Assert.AreEqual(2L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM SchemaVersions;"));

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
    public async Task SchemaRejectsNonVersionSevenOrInvalidVariantIdentifiers(string invalidIdentifier)
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
                session_id, counter_epoch, last_incident_points_total,
                last_replay_session_number, last_replay_session_time_ms, updated_at_utc_ms)
            VALUES (@sessionId, 0, 2, 0, 1000, 1000);
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
    public async Task PreferencesEnforceSingletonBooleanAndPlaybackValues()
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
    }

    [TestMethod]
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
                updated_at_utc_ms = 253402300799999
            WHERE preferences_id = 1;
            """,
            ("@camera", new string('c', 128)));
        InsertStoreOperation(connection, OperationId.Create().ToString());

        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM Incident;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
    }

    [TestMethod]
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
            2 => "PRAGMA user_version = 2;",
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
