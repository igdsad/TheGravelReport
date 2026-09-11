using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.DependencyInjection;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Http.Tests;

[TestClass]
public sealed class HttpEventSyncTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task RegistrationDoesNotCapTheConfiguredTwoMinuteRequestTimeout()
    {
        var options = EventSyncHttpOptions.TryCreate(TimeSpan.FromMinutes(2)).Value;
        var services = new ServiceCollection();
        services.AddHttpEventSync(options);

        await using var provider = services.BuildServiceProvider();

        Assert.AreEqual(
            Timeout.InfiniteTimeSpan,
            provider.GetRequiredService<HttpClient>().Timeout);
        Assert.AreEqual(
            TimeSpan.FromMinutes(2),
            provider.GetRequiredService<EventSyncHttpOptions>().RequestTimeout);
        Assert.IsNotNull(provider.GetRequiredService<ICustomEventPublisher>());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("league-night/")]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task HostAndPublisherReturnFirstAcceptedThenDuplicate(string advertisedPath)
    {
        var options = EventSyncHttpOptions.TryCreate(TimeSpan.FromSeconds(5)).Value;
        var services = new ServiceCollection();
        services.AddHttpEventSync(options);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<ICustomEventSessionHost>();
        var publisher = provider.GetRequiredService<ICustomEventPublisher>();
        var handler = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var request = CustomEventSessionHostRequest.TryCreate(
            session,
            new Uri($"http://127.0.0.1:{port}/"),
            new Uri($"http://127.0.0.1:{port}/{advertisedPath}")).Value;
        var first = CreateSubmission(session, "Driver One");
        var competing = CreateSubmission(session, "Driver Two");

        var started = await host.StartAsync(request, handler, CancellationToken.None);

        Assert.IsTrue(started.IsSuccess);
        Assert.IsFalse(host.Completion.IsCompleted);
        var accepted = await publisher.PublishAsync(
            started.Value,
            first,
            CancellationToken.None);
        var duplicate = await publisher.PublishAsync(
            started.Value,
            competing,
            CancellationToken.None);

        Assert.IsTrue(accepted.IsSuccess);
        Assert.AreEqual(CustomEventPublishOutcome.Accepted, accepted.Value);
        Assert.IsTrue(duplicate.IsSuccess);
        Assert.AreEqual(CustomEventPublishOutcome.Duplicate, duplicate.Value);
        Assert.AreEqual(first, handler.Events[first.Id]);
        Assert.HasCount(1, handler.Events);

        var completion = host.Completion;
        var stopped = await host.StopAsync(CancellationToken.None);

        Assert.IsTrue(stopped.IsSuccess);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task GenericReceiverReturnsFirstAcceptedThenDuplicate()
    {
        var options = EventSyncHttpOptions.TryCreate(TimeSpan.FromSeconds(5)).Value;
        var receiver = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        await using var application = BuildGenericReceiver(baseUri, receiver, options);
        await application.StartAsync(CancellationToken.None);

        try
        {
            var services = new ServiceCollection();
            services.AddHttpEventSync(options);
            await using var provider = services.BuildServiceProvider();
            var publisher = provider.GetRequiredService<ICustomEventPublisher>();
            var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
            var joinCode = JoinCode.TryCreate(baseUri, session).Value;
            var first = CreateSubmission(session, "Driver One");
            var competing = CreateSubmission(session, "Driver Two");
            var otherSession = CreateSession("74738ff5-5367-5958-9aee-98fffdcd1876");
            var other = CreateSubmission(otherSession, "Driver Three");

            var accepted = await publisher.PublishAsync(
                joinCode,
                first,
                CancellationToken.None);
            var duplicate = await publisher.PublishAsync(
                joinCode,
                competing,
                CancellationToken.None);
            var otherAccepted = await publisher.PublishAsync(
                JoinCode.TryCreate(baseUri, otherSession).Value,
                other,
                CancellationToken.None);

            Assert.IsTrue(accepted.IsSuccess);
            Assert.AreEqual(CustomEventPublishOutcome.Accepted, accepted.Value);
            Assert.IsTrue(duplicate.IsSuccess);
            Assert.AreEqual(CustomEventPublishOutcome.Duplicate, duplicate.Value);
            Assert.IsTrue(otherAccepted.IsSuccess);
            Assert.AreEqual(CustomEventPublishOutcome.Accepted, otherAccepted.Value);
            Assert.AreEqual(first, receiver.Events[first.Id]);
            Assert.AreEqual(other, receiver.Events[other.Id]);
            Assert.HasCount(2, receiver.Events);
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task GenericReceiverRejectsRouteAndBodySessionMismatch()
    {
        var receiver = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        await using var application = BuildGenericReceiver(baseUri, receiver);
        await application.StartAsync(CancellationToken.None);

        try
        {
            using var client = new HttpClient();
            var routeSession = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
            var bodySession = CreateSession("74738ff5-5367-5958-9aee-98fffdcd1876");
            using var content = CreateWireContent(CreateSubmission(bodySession, "Driver One"));
            using var response = await client.PostAsync(
                new Uri(
                    baseUri,
                    $"event-sync/v1/sessions/{routeSession}/events"),
                content,
                CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(
                await response.Content.ReadAsStringAsync(CancellationToken.None),
                EventSyncErrorCodes.SessionMismatch.Value,
                StringComparison.Ordinal);
            Assert.IsEmpty(receiver.Events);
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public async Task GenericReceiverRejectsInvalidAndNonDeterministicSessionRoutes()
    {
        var receiver = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        await using var application = BuildGenericReceiver(baseUri, receiver);
        await application.StartAsync(CancellationToken.None);

        try
        {
            using var client = new HttpClient();
            var bodySession = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
            var submission = CreateSubmission(bodySession, "Driver One");
            var invalidRoutes = new[]
            {
                "not-a-session",
                SessionIdentity.Generate().ToString(),
            };

            foreach (var route in invalidRoutes)
            {
                using var content = CreateWireContent(submission);
                using var response = await client.PostAsync(
                    new Uri(baseUri, $"event-sync/v1/sessions/{route}/events"),
                    content,
                    CancellationToken.None);

                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            }

            Assert.IsEmpty(receiver.Events);
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [DataRow("replaySessionNumber")]
    [DataRow("replaySessionTimeMilliseconds")]
    [DataRow("occurredAtUnixMilliseconds")]
    [TestProperty("Requirement", "QR-SEC-001")]
    public async Task GenericReceiverRejectsMissingRequiredNumericFields(string missingField)
    {
        var receiver = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        await using var application = BuildGenericReceiver(baseUri, receiver);
        await application.StartAsync(CancellationToken.None);

        try
        {
            using var client = new HttpClient();
            var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
            var position = ReplayPosition.TryCreate(
                SessionNumber.TryCreate(0).Value,
                SessionTime.TryCreateMilliseconds(0).Value).Value;
            var submission = CustomEventSubmission.TryCreate(
                CustomEventId.CreateDeterministic(session, position),
                session,
                position,
                SubmitterName.TryCreate("Driver One").Value,
                UtcInstant.TryCreateUnixMilliseconds(0).Value).Value;
            var payload = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["eventId"] = submission.Id.ToString(),
                ["sessionIdentity"] = submission.SessionIdentity.ToString(),
                ["replaySessionNumber"] = 0,
                ["replaySessionTimeMilliseconds"] = 0L,
                ["eventType"] = CustomEventId.EventType,
                ["submitterName"] = submission.Submitter.Value,
                ["occurredAtUnixMilliseconds"] = 0L,
            };
            Assert.IsTrue(payload.Remove(missingField));
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(
                new Uri(
                    baseUri,
                    $"event-sync/v1/sessions/{session}/events"),
                content,
                CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.IsEmpty(receiver.Events);
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public async Task PublisherRejectsSessionMismatchBeforeSending()
    {
        var sender = new RecordingHttpMessageHandler(static _ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(sender));
        services.AddHttpEventSync();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ICustomEventPublisher>();
        var joinedSession = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var eventSession = CreateSession("74738ff5-5367-5958-9aee-98fffdcd1876");
        var joinCode = JoinCode.TryCreate(
            new Uri("https://review.example.test/"),
            joinedSession).Value;

        var result = await publisher.PublishAsync(
            joinCode,
            CreateSubmission(eventSession, "Driver One"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EventSyncErrorCodes.SessionMismatch, result.Error?.Code);
        Assert.AreEqual(0, sender.CallCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task PublisherRejectsSuccessStatusWithInconsistentOutcome()
    {
        var sender = new RecordingHttpMessageHandler(static _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"schemaVersion\":1,\"outcome\":\"accepted\"}",
                    Encoding.UTF8,
                    "application/json"),
            });
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(sender));
        services.AddHttpEventSync();
        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ICustomEventPublisher>();
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var joinCode = JoinCode.TryCreate(
            new Uri("https://review.example.test/"),
            session).Value;

        var result = await publisher.PublishAsync(
            joinCode,
            CreateSubmission(session, "Driver One"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EventSyncHttpErrorCodes.InvalidResponse, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Integration, result.Error?.Kind);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SEC-001")]
    public async Task HostRejectsAnOversizedRequestBeforeCallingReceiver()
    {
        var options = EventSyncHttpOptions.TryCreate(
            TimeSpan.FromSeconds(5),
            maximumRequestBodyBytes: 1024).Value;
        var services = new ServiceCollection();
        services.AddHttpEventSync(options);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<ICustomEventSessionHost>();
        var receiver = new FirstWinsHandler();
        var port = ReserveAvailablePort();
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        var request = CustomEventSessionHostRequest.TryCreate(
            session,
            baseUri,
            baseUri).Value;
        var started = await host.StartAsync(request, receiver, CancellationToken.None);
        Assert.IsTrue(started.IsSuccess);

        try
        {
            using var client = new HttpClient();
            using var content = new StringContent(
                new string('x', 1025),
                Encoding.UTF8,
                "application/json");
            var endpoint = new Uri(
                baseUri,
                $"event-sync/v1/sessions/{session}/events");

            using var response = await client.PostAsync(
                endpoint,
                content,
                CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.IsEmpty(receiver.Events);
        }
        finally
        {
            var stopped = await host.StopAsync(CancellationToken.None);
            Assert.IsTrue(stopped.IsSuccess);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void OptionsRejectEveryOutOfRangeDimension()
    {
        Assert.IsFalse(EventSyncHttpOptions.TryCreate(TimeSpan.Zero).IsSuccess);
        Assert.IsFalse(EventSyncHttpOptions.TryCreate(TimeSpan.FromMinutes(3)).IsSuccess);
        Assert.IsFalse(EventSyncHttpOptions.TryCreate(
            TimeSpan.FromSeconds(1),
            maximumRequestBodyBytes: 100).IsSuccess);
        Assert.IsFalse(EventSyncHttpOptions.TryCreate(
            TimeSpan.FromSeconds(1),
            maximumResponseBodyBytes: 100).IsSuccess);
    }

    private static CustomEventSubmission CreateSubmission(
        SessionIdentity session,
        string submitterName)
    {
        var position = ReplayPosition.TryCreate(
            SessionNumber.TryCreate(1).Value,
            SessionTime.TryCreateMilliseconds(12_345).Value).Value;
        return CustomEventSubmission.TryCreate(
            CustomEventId.CreateDeterministic(session, position),
            session,
            position,
            SubmitterName.TryCreate(submitterName).Value,
            UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_000).Value).Value;
    }

    private static WebApplication BuildGenericReceiver(
        Uri baseUri,
        ICustomEventReceiver receiver,
        EventSyncHttpOptions? options = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HttpEventSyncTests).Assembly.FullName,
            Args = [],
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(baseUri.GetLeftPart(UriPartial.Authority));
        builder.Services.AddSingleton(receiver);

        var application = builder.Build();
        application.MapHttpEventSyncReceiver(options);
        return application;
    }

    private static StringContent CreateWireContent(CustomEventSubmission submission)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            eventId = submission.Id.ToString(),
            sessionIdentity = submission.SessionIdentity.ToString(),
            replaySessionNumber = submission.ReplayPosition.SessionNumber.Value,
            replaySessionTimeMilliseconds = submission.ReplayPosition.SessionTime.Milliseconds,
            eventType = CustomEventId.EventType,
            submitterName = submission.Submitter.Value,
            occurredAtUnixMilliseconds = submission.OccurredAt.UnixMilliseconds,
        });
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static SessionIdentity CreateSession(string value) =>
        SessionIdentity.TryCreate(Guid.ParseExact(value, "D")).Value;

    private static int ReserveAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class FirstWinsHandler : ICustomEventReceiver
    {
        public ConcurrentDictionary<CustomEventId, CustomEventSubmission> Events { get; } = new();

        public Task<Result<CustomEventPublishOutcome>> ReceiveAsync(
            CustomEventSubmission submission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = Events.TryAdd(submission.Id, submission)
                ? CustomEventPublishOutcome.Accepted
                : CustomEventPublishOutcome.Duplicate;
            return Task.FromResult(Result<CustomEventPublishOutcome>.Success(outcome));
        }
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(respond(request));
        }
    }
}
