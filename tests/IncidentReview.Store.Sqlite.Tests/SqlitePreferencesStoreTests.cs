using IncidentReview.Domain;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.DependencyInjection;
using IncidentReview.Store.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.Sqlite.Tests;

[TestClass]
public sealed class SqlitePreferencesStoreTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void DependencyInjectionRegistersStoreAndInitializerAsSingletonContracts()
    {
        using var database = new TemporarySqliteDatabase();
        IServiceCollection services = new ServiceCollection();

        _ = services.AddSqliteStore(database.Options);

        Assert.AreEqual(
            ServiceLifetime.Singleton,
            services.Single(descriptor => descriptor.ServiceType == typeof(IStore)).Lifetime);
        Assert.AreEqual(
            ServiceLifetime.Singleton,
            services.Single(descriptor => descriptor.ServiceType == typeof(IStoreInitializer)).Lifetime);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task StoreGateRejectsOperationsUntilInitializationSucceeds()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = SqliteTestingRegistration.CreateStore(database.Options);

        var queryResult = await context.Store.QueryAsync(
            GetPreferences.Instance,
            CancellationToken.None);
        var commandResult = await context.Store.ExecuteAsync(
            CreateUpdate(CreatePreferences(2_000, 0.5, autoPause: false, "Cockpit"), 100),
            CancellationToken.None);

        AssertFailure(queryResult, StoreErrorCodes.NotInitialized, ErrorKind.Unavailable);
        AssertFailure(commandResult, StoreErrorCodes.NotInitialized, ErrorKind.Unavailable);
        Assert.IsFalse(File.Exists(database.DatabasePath));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task InitializedStoreReturnsDefaultsAndRoundTripsAnUpdate()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);

        var defaults = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);
        Assert.IsTrue(defaults.IsSuccess);
        AssertPreferences(defaults.Value, 5_000, 1.0, autoPause: true, expectedCamera: null);

        var expected = CreatePreferences(12_345, 0.5, autoPause: false, "Cockpit");
        var update = CreateUpdate(expected, 1_234_567);
        var updateResult = await context.Store.ExecuteAsync(update, CancellationToken.None);
        var actual = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);

        Assert.IsTrue(updateResult.IsSuccess);
        Assert.IsTrue(actual.IsSuccess);
        Assert.AreEqual(expected, actual.Value);
        using var connection = database.OpenConnection();
        Assert.AreEqual(1_234_567L, ExecuteScalarInt64(
            connection,
            "SELECT updated_at_utc_ms FROM ApplicationPreferences WHERE preferences_id = 1;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-005")]
    [TestProperty("Requirement", "IR-STR-006")]
    public async Task IdenticalRetryHasOneCommittedEffectAndReconcilesAsCommitted()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var command = CreateUpdate(
            CreatePreferences(10_000, 0.75, autoPause: true, "Far Chase"),
            2_000);

        var first = await context.Store.ExecuteAsync(command, CancellationToken.None);
        var retry = await context.Store.ExecuteAsync(command, CancellationToken.None);
        var outcome = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(retry.IsSuccess);
        Assert.IsTrue(outcome.IsSuccess);
        Assert.AreSame(OperationOutcome.Committed, outcome.Value);
        using var connection = database.OpenConnection();
        Assert.AreEqual(1L, ExecuteScalarInt64(
            connection,
            "SELECT COUNT(*) FROM StoreOperation;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-005")]
    public async Task ReusedOperationIdentityWithDifferentFingerprintReturnsConflict()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var command = CreateUpdate(
            CreatePreferences(9_000, 0.5, autoPause: false, "Chopper"),
            3_000);
        using (var connection = database.OpenConnection())
        {
            ExecuteParameterized(
                connection,
                """
                INSERT INTO StoreOperation (
                    operation_id, command_kind, command_version,
                    payload_fingerprint_sha256, committed_at_utc_ms)
                VALUES (@operationId, 'different.command', 1, @fingerprint, 0);
                """,
                ("@operationId", command.OperationId.ToString()),
                ("@fingerprint", new byte[32]));
        }

        var result = await context.Store.ExecuteAsync(command, CancellationToken.None);

        AssertFailure(result, StoreErrorCodes.OperationIdConflict, ErrorKind.Conflict);
        var preferences = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);
        Assert.IsTrue(preferences.IsSuccess);
        AssertPreferences(preferences.Value, 5_000, 1.0, autoPause: true, expectedCamera: null);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task PreCanceledCommandThrowsAndLeavesNoEffect()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var command = CreateUpdate(
            CreatePreferences(1_000, 0.25, autoPause: false, null),
            4_000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            context.Store.ExecuteAsync(command, cancellation.Token));

        using var connection = database.OpenConnection();
        Assert.AreEqual(0L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
        Assert.AreEqual(5_000L, ExecuteScalarInt64(
            connection,
            "SELECT replay_lead_in_ms FROM ApplicationPreferences WHERE preferences_id = 1;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task UnknownTypedRequestsReturnSafeUnsupportedFailures()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);

        var query = await context.Store.QueryAsync(new UnknownQuery(), CancellationToken.None);
        var command = await context.Store.ExecuteAsync(new UnknownCommand(), CancellationToken.None);

        AssertFailure(query, StoreErrorCodes.UnsupportedRequest, ErrorKind.Validation);
        AssertFailure(command, StoreErrorCodes.UnsupportedRequest, ErrorKind.Validation);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task ProviderFailureIsTranslatedWithoutLeakingProviderDetails()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        File.Delete(database.DatabasePath);

        var result = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);

        AssertFailure(result, StoreErrorCodes.PersistenceFailure, ErrorKind.Persistence);
        Assert.IsFalse(result.Error!.Message.Contains(database.DatabasePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Error.Message.Contains("ApplicationPreferences", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task ConcurrentCommandsAreSerializedWithoutLostOperations()
    {
        using var database = new TemporarySqliteDatabase(executorCapacity: 2);
        await using var context = await CreateInitializedStore(database);
        var commands = Enumerable.Range(0, 32)
            .Select(index => CreateUpdate(
                CreatePreferences(index * 100, 0.5, index % 2 == 0, $"Camera {index}"),
                index + 1L))
            .ToArray();

        var results = await Task.WhenAll(commands.Select(command =>
            context.Store.ExecuteAsync(command, CancellationToken.None)));

        Assert.IsTrue(results.All(static result => result.IsSuccess));
        using var connection = database.OpenConnection();
        Assert.AreEqual(32L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM ApplicationPreferences;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    [DataRow(SqliteTestCommitMode.IndeterminateBeforeCommit, false)]
    [DataRow(SqliteTestCommitMode.IndeterminateAfterCommit, true)]
    public async Task IndeterminateCommitCanBeReconciled(
        SqliteTestCommitMode mode,
        bool expectedCommitted)
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = SqliteTestingRegistration.CreateStore(database.Options, mode);
        Assert.IsTrue((await context.Initializer.InitializeAsync(CancellationToken.None)).IsSuccess);
        var command = CreateUpdate(
            CreatePreferences(7_000, 0.25, autoPause: false, "Scenic"),
            5_000);

        var result = await context.Store.ExecuteAsync(command, CancellationToken.None);
        var outcome = await context.Store.QueryAsync(
            new GetOperationOutcome(command.OperationId),
            CancellationToken.None);

        AssertFailure(result, StoreErrorCodes.IndeterminateCommit, ErrorKind.Indeterminate);
        Assert.IsTrue(outcome.IsSuccess);
        Assert.AreEqual(expectedCommitted, outcome.Value.IsCommitted);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    public async Task CleanupFailuresDoNotReplaceThePrimaryIndeterminateOutcome()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = SqliteTestingRegistration.CreateStore(
            database.Options,
            SqliteTestCommitMode.IndeterminateBeforeCommit,
            SqliteTestCleanupMode.FailAfterCleanup);
        Assert.IsTrue((await context.Initializer.InitializeAsync(CancellationToken.None)).IsSuccess);
        var command = CreateUpdate(CreatePreferences(8_000, 0.5, false, null), 5_500);

        var result = await context.Store.ExecuteAsync(command, CancellationToken.None);

        AssertFailure(result, StoreErrorCodes.IndeterminateCommit, ErrorKind.Indeterminate);
        using var connection = database.OpenConnection();
        Assert.AreEqual(0L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-005")]
    public async Task PreferencesFingerprintMatchesVersionedGoldenVector()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = await CreateInitializedStore(database);
        var command = CreateUpdate(
            CreatePreferences(12_345, 0.5, autoPause: true, "Cockpit"),
            1_234_567);

        Assert.IsTrue((await context.Store.ExecuteAsync(command, CancellationToken.None)).IsSuccess);

        using var connection = database.OpenConnection();
        Assert.AreEqual(
            "c77f3faa749b7344795cde107cf08dd026149819876b0a3f1f0d5dbf3e99b84c",
            ExecuteScalarString(
                connection,
                "SELECT lower(hex(payload_fingerprint_sha256)) FROM StoreOperation;"));
        Assert.AreEqual(
            "preferences.update",
            ExecuteScalarString(connection, "SELECT command_kind FROM StoreOperation;"));
        Assert.AreEqual(1L, ExecuteScalarInt64(connection, "SELECT command_version FROM StoreOperation;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-003")]
    public async Task DisposalDrainsAdmittedWorkAndClosesTheExecutor()
    {
        using var database = new TemporarySqliteDatabase(executorCapacity: 32);
        var context = await CreateInitializedStore(database);
        var operations = Enumerable.Range(0, 16)
            .Select(index => context.Store.ExecuteAsync(
                CreateUpdate(CreatePreferences(index, 0.5, false, null), index + 1L),
                CancellationToken.None))
            .ToArray();

        await context.DisposeAsync();
        var results = await Task.WhenAll(operations);
        var afterDispose = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);
        await context.DisposeAsync();

        Assert.IsTrue(results.All(static result => result.IsSuccess));
        AssertFailure(afterDispose, StoreErrorCodes.NotInitialized, ErrorKind.Unavailable);
        using var connection = database.OpenConnection();
        Assert.AreEqual(16L, ExecuteScalarInt64(connection, "SELECT COUNT(*) FROM StoreOperation;"));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task UnexpectedExecutorDefectFaultsCallerAndWorkerRemainsObserved()
    {
        using var database = new TemporarySqliteDatabase();
        await using var context = SqliteTestingRegistration.CreateStore(
            database.Options,
            (SqliteTestCommitMode)int.MaxValue);
        Assert.IsTrue((await context.Initializer.InitializeAsync(CancellationToken.None)).IsSuccess);
        var command = CreateUpdate(CreatePreferences(1_000, 0.5, false, null), 6_000);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            context.Store.ExecuteAsync(command, CancellationToken.None));
        var nextQuery = await context.Store.QueryAsync(GetPreferences.Instance, CancellationToken.None);

        Assert.IsTrue(nextQuery.IsSuccess);
        AssertPreferences(nextQuery.Value, 5_000, 1.0, autoPause: true, expectedCamera: null);
    }

    private static async Task<SqliteTestStoreContext> CreateInitializedStore(
        TemporarySqliteDatabase database)
    {
        var context = SqliteTestingRegistration.CreateStore(database.Options);
        var initialization = await context.Initializer.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(initialization.IsSuccess, initialization.Error?.ToString());
        return context;
    }

    private static UpdatePreferences CreateUpdate(UserPreferences preferences, long updatedAtUnixMilliseconds) =>
        UpdatePreferences.Create(
            preferences,
            UtcInstant.TryCreateUnixMilliseconds(updatedAtUnixMilliseconds).Value);

    private static UserPreferences CreatePreferences(
        long leadInMilliseconds,
        double playbackSpeed,
        bool autoPause,
        string? camera) =>
        UserPreferences.TryCreateMilliseconds(
            leadInMilliseconds,
            playbackSpeed,
            autoPause,
            camera).Value;

    private static void AssertPreferences(
        UserPreferences actual,
        long expectedLeadIn,
        double expectedSpeed,
        bool autoPause,
        string? expectedCamera)
    {
        Assert.AreEqual(expectedLeadIn, actual.ReplayLeadInMilliseconds);
        Assert.AreEqual(expectedSpeed, actual.PlaybackSpeed);
        Assert.AreEqual(autoPause, actual.AutoPause);
        Assert.AreEqual(expectedCamera, actual.PreferredCamera);
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

    private static void AssertFailure(Result result, ErrorCode code, ErrorKind kind)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(kind, result.Error.Kind);
    }

    private static void AssertFailure<T>(Result<T> result, ErrorCode code, ErrorKind kind)
        where T : notnull
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(kind, result.Error.Kind);
    }

    private sealed class UnknownQuery : IStoreQuery<UserPreferences>
    {
    }

    private sealed class UnknownCommand : IStoreCommand
    {
        public OperationId OperationId { get; } = OperationId.Create();
    }
}
