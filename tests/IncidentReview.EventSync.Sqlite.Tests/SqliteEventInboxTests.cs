using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Sqlite.DependencyInjection;
using IncidentReview.EventSync.Sqlite.Options;
using IncidentReview.EventSync.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IncidentReview.EventSync.Sqlite.Tests;

[TestClass]
public sealed class SqliteEventInboxTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task HostedLifecycleInitializesSchemaBeforeAcceptingTraffic()
    {
        await using var fixture = InboxFixture.Create();
        var submission = CreateSubmission("Driver One", 1_800_000_000_000);

        var beforeStart = await fixture.Receiver.ReceiveAsync(submission, CancellationToken.None);

        Assert.IsFalse(beforeStart.IsSuccess);
        Assert.AreEqual(SqliteEventInboxErrorCodes.NotReady, beforeStart.Error?.Code);
        Assert.IsFalse(File.Exists(fixture.DatabasePath));

        await fixture.StartAsync();
        var afterStart = await fixture.Receiver.ReceiveAsync(submission, CancellationToken.None);

        Assert.IsTrue(afterStart.IsSuccess, afterStart.Error?.ToString());
        Assert.AreEqual(CustomEventPublishOutcome.Accepted, afterStart.Value);
        Assert.IsTrue(File.Exists(fixture.DatabasePath));
        using var connection = fixture.OpenReadOnlyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM EventSyncSchema WHERE singleton = 1;";
        Assert.AreEqual(1L, command.ExecuteScalar());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task ExactIdentityRetainsFirstPayloadAndReportsLaterSubmissionAsDuplicate()
    {
        await using var fixture = InboxFixture.Create();
        await fixture.StartAsync();
        var first = CreateSubmission("First Driver", 1_800_000_000_001);
        var later = CreateSubmission("Later Driver", 1_800_000_099_999);

        var accepted = await fixture.Receiver.ReceiveAsync(first, CancellationToken.None);
        var duplicate = await fixture.Receiver.ReceiveAsync(later, CancellationToken.None);

        Assert.IsTrue(accepted.IsSuccess);
        Assert.AreEqual(CustomEventPublishOutcome.Accepted, accepted.Value);
        Assert.IsTrue(duplicate.IsSuccess);
        Assert.AreEqual(CustomEventPublishOutcome.Duplicate, duplicate.Value);

        using var connection = fixture.OpenReadOnlyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT custom_event_id,
                   session_id,
                   replay_session_number,
                   session_time_ms,
                   submitter_name,
                   occurred_at_unix_ms
            FROM CustomEventInbox;
            """;
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(first.Id.ToString(), reader.GetString(0));
        Assert.AreEqual(first.SessionIdentity.ToString(), reader.GetString(1));
        Assert.AreEqual(first.ReplayPosition.SessionNumber.Value, reader.GetInt32(2));
        Assert.AreEqual(first.ReplayPosition.SessionTime.Milliseconds, reader.GetInt64(3));
        Assert.AreEqual(first.Submitter.Value, reader.GetString(4));
        Assert.AreEqual(first.OccurredAt.UnixMilliseconds, reader.GetInt64(5));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task ConcurrentSubmissionsForOneIdentityProduceExactlyOneAcceptedRecord()
    {
        await using var fixture = InboxFixture.Create(executorCapacity: 128);
        await fixture.StartAsync();
        var submission = CreateSubmission("Concurrent Driver", 1_800_000_000_002);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => fixture.Receiver.ReceiveAsync(submission, CancellationToken.None)));

        Assert.IsTrue(results.All(static result => result.IsSuccess));
        Assert.AreEqual(
            1,
            results.Count(static result => result.Value == CustomEventPublishOutcome.Accepted));
        Assert.AreEqual(
            63,
            results.Count(static result => result.Value == CustomEventPublishOutcome.Duplicate));

        using var connection = fixture.OpenReadOnlyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CustomEventInbox;";
        Assert.AreEqual(1L, command.ExecuteScalar());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task PersistenceFailureReturnsSafeResultWithoutProviderDetails()
    {
        await using var fixture = InboxFixture.Create();
        await fixture.StartAsync();
        File.Delete(fixture.DatabasePath);

        var result = await fixture.Receiver.ReceiveAsync(
            CreateSubmission("Driver One", 1_800_000_000_003),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SqliteEventInboxErrorCodes.PersistenceUnavailable, result.Error?.Code);
        Assert.IsFalse(
            (result.Error?.Message ?? string.Empty).Contains(
                fixture.DatabasePath,
                StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task UnavailableDataDirectoryFailsStartupAndKeepsReceiverClosed()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "GravelReview.Tests",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var blockingFile = Path.Combine(directory, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "block");
        var databasePath = Path.Combine(blockingFile, "events.db");
        await using var fixture = InboxFixture.Create(
            databasePath: databasePath,
            cleanupDirectoryPath: directory);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            fixture.StartAsync);
        var receive = await fixture.Receiver.ReceiveAsync(
            CreateSubmission("Driver One", 1_800_000_000_004),
            CancellationToken.None);

        Assert.AreEqual("The SQLite event inbox could not be initialized.", exception.Message);
        Assert.IsFalse(receive.IsSuccess);
        Assert.AreEqual(SqliteEventInboxErrorCodes.NotReady, receive.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task UnexpectedWorkerFaultStopsTheGenericHost()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "GravelReview.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "events.db");
        var options = SqliteEventInboxOptions.TryCreate(databasePath);
        Assert.IsTrue(options.IsSuccess);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddFaultingSqliteEventSyncReceiver(options.Value);
            using var host = builder.Build();
            await host.StartAsync();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var stopping = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = lifetime.ApplicationStopping.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                stopping);

            var result = await host.Services
                .GetRequiredService<ICustomEventReceiver>()
                .ReceiveAsync(
                    CreateSubmission("Driver One", 1_800_000_000_005),
                    CancellationToken.None);
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(
                SqliteEventInboxErrorCodes.PersistenceUnavailable,
                result.Error?.Code);
            Assert.IsTrue(lifetime.ApplicationStopping.IsCancellationRequested);
            await host.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CustomEventSubmission CreateSubmission(
        string submitter,
        long occurredAtUnixMilliseconds)
    {
        var session = SessionIdentity.TryParse("21f7f8de-8051-5b89-8680-0195ef798b6a").Value;
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(2).Value,
            SessionTime.TryCreateMilliseconds(73_456).Value).Value;
        return CustomEventSubmission.TryCreate(
            CustomEventId.CreateDeterministic(session, position),
            session,
            position,
            SubmitterName.TryCreate(submitter).Value,
            UtcInstant.TryCreateUnixMilliseconds(occurredAtUnixMilliseconds).Value).Value;
    }

    private sealed class InboxFixture : IAsyncDisposable
    {
        private readonly string _directoryPath;
        private readonly ServiceProvider _provider;
        private readonly IHostedService _lifecycle;
        private bool _started;

        private InboxFixture(
            string directoryPath,
            string databasePath,
            ServiceProvider provider)
        {
            _directoryPath = directoryPath;
            DatabasePath = databasePath;
            _provider = provider;
            Receiver = provider.GetRequiredService<ICustomEventReceiver>();
            _lifecycle = provider.GetServices<IHostedService>().Single();
        }

        public string DatabasePath { get; }

        public ICustomEventReceiver Receiver { get; }

        public static InboxFixture Create(
            int executorCapacity = 256,
            string? databasePath = null,
            string? cleanupDirectoryPath = null)
        {
            var directoryPath = databasePath is null
                ? Path.Combine(
                    Path.GetTempPath(),
                    "GravelReview.Tests",
                    Guid.NewGuid().ToString("N"))
                : Path.GetDirectoryName(databasePath) ?? Path.GetTempPath();
            databasePath ??= Path.Combine(directoryPath, "events.db");
            cleanupDirectoryPath ??= directoryPath;
            var options = SqliteEventInboxOptions.TryCreate(
                databasePath,
                executorCapacity: executorCapacity);
            Assert.IsTrue(options.IsSuccess);

            var services = new ServiceCollection();
            services.AddSqliteEventSyncReceiver(options.Value);
            var provider = services.BuildServiceProvider();
            return new InboxFixture(cleanupDirectoryPath, databasePath, provider);
        }

        public async Task StartAsync()
        {
            await _lifecycle.StartAsync(CancellationToken.None);
            _started = true;
        }

        public SqliteConnection OpenReadOnlyConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            if (_started)
            {
                await _lifecycle.StopAsync(CancellationToken.None);
            }

            await _provider.DisposeAsync();
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "GravelReview.Tests");
            if (Directory.Exists(_directoryPath)
                && Path.GetFullPath(_directoryPath).StartsWith(
                    Path.GetFullPath(cleanupRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }
    }
}
