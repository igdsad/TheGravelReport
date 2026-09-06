using IncidentReview.Domain;
using IncidentReview.Iracing.Options;
using IncidentReview.Iracing.Testing;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TelemetryIntegrationTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_123);

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncDecodesLiveReplayIdentityAndReconnectTransitions()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var source = CreateSource(simulator);
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        var initial = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;

        Assert.AreEqual(SessionMode.Live, initial.Session.Mode);
        Assert.AreEqual(2, initial.Position.SessionNumber.Value);
        Assert.AreEqual(12_345, initial.Position.SessionTime.Milliseconds);
        Assert.AreEqual(
            4,
            initial.IncidentCounter.Value,
            "The independent frame carries team=9; only local-member count=4 is valid here.");
        Assert.AreEqual(7, initial.Lap?.Value);
        Assert.AreEqual(0.25d, initial.LapDistance?.Value);
        Assert.AreEqual(OnTrackState.OnTrack, initial.OnTrackState);
        Assert.AreEqual(ObservedAt.ToUnixTimeMilliseconds(), initial.ObservedAt.UnixMilliseconds);
        Assert.AreEqual(SimulatorIdentityScope.Durable, initial.Session.IdentityScope);
        Assert.AreEqual(
            "v1:subsession:987654321:session:2",
            initial.Session.SessionKey.Value);

        await simulator.SendAsync("publish 6 13.999999");
        var fractional = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(13_999, fractional.Position.SessionTime.Milliseconds);
        Assert.AreEqual(6, fractional.IncidentCounter.Value);

        await simulator.SendAsync("replay 5 22.2229 8");
        var replay = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(SessionMode.Replay, replay.Session.Mode);
        Assert.AreEqual(5, replay.Position.SessionNumber.Value);
        Assert.AreEqual(22_222, replay.Position.SessionTime.Milliseconds);
        Assert.AreEqual(
            "v1:subsession:987654321:session:5",
            replay.Session.SessionKey.Value);

        await simulator.SendAsync("identity none");
        var scoped = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(SimulatorIdentityScope.ConnectionScoped, scoped.Session.IdentityScope);
        StringAssert.StartsWith(scoped.Session.SessionKey.Value, "connection:");

        await simulator.SendAsync("publish 9 30.5");
        var sameConnection = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(scoped.Session.SessionKey, sameConnection.Session.SessionKey);

        await simulator.SendAsync("disconnect");
        Assert.IsInstanceOfType<TelemetryDisconnected>(await NextAsync(observer));
        await simulator.SendAsync("reconnect");
        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        var reconnected = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreNotEqual(scoped.Session.SessionKey, reconnected.Session.SessionKey);

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncHonorsOfficialSessionInformationEncodingSignal()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var source = CreateSource(simulator);
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        _ = Assert.IsInstanceOfType<TelemetrySampleObserved>(await NextAsync(observer));

        await simulator.SendAsync("identity-legacy 111222333");
        var legacy = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(
            "v1:subsession:111222333:session:2",
            legacy.Session.SessionKey.Value,
            "Without an Encoding: UTF8 signal, 0xE9 in René must decode as ISO-8859-1.");

        await simulator.SendAsync("identity-utf8 444555666");
        var utf8 = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(
            "v1:subsession:444555666:session:2",
            utf8.Session.SessionKey.Value,
            "The official UTF8 signal must select strict UTF-8 decoding.");

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-001")]
    [TestProperty("Requirement", "IR-SES-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow("invalid-bool", "publish 6 14.0")]
    [DataRow("invalid-utf8-session", "identity none")]
    [DataRow("malformed", "repair")]
    [DataRow("nan-time", "publish 6 14.0")]
    public async Task InvalidFrameRetainsConnectionScopedSessionAcrossRecovery(
        string invalidCommand,
        string recoveryCommand)
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var integration = IracingTestingRegistration.CreateIntegrationContext(
            simulator.MemoryMapName,
            simulator.EventName,
            FastOptions());
        var source = integration.Telemetry;
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        _ = await NextAsync(observer);
        _ = await NextAsync(observer);
        await simulator.SendAsync("identity none");
        var before = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;

        await simulator.SendAsync(invalidCommand);

        var unavailable = Assert.IsInstanceOfType<TelemetryUnavailable>(await NextAsync(observer));
        Assert.AreEqual(IracingErrorCodes.InvalidTelemetryFrame, unavailable.Error.Code);
        Assert.IsFalse(integration.ContextReader.Read().IsSuccess);

        await simulator.SendAsync(recoveryCommand);
        var recovered = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(before.Session.SessionKey, recovered.Session.SessionKey);
        Assert.AreEqual(
            SimulatorIdentityScope.ConnectionScoped,
            recovered.Session.IdentityScope);
        Assert.IsTrue(integration.ContextReader.Read().IsSuccess);

        await simulator.SendAsync("disconnect");
        Assert.IsInstanceOfType<TelemetryDisconnected>(await NextAsync(observer));
        await simulator.SendAsync("reconnect");
        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        var reconnected = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreNotEqual(before.Session.SessionKey, reconnected.Session.SessionKey);

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task PersistentInvalidFrameEndsLogicalConnectionOnlyAtStaleDeadline()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var timeProvider = new ManualTimeProvider(ObservedAt);
        var source = IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            timeProvider,
            FastOptions(eventBufferCapacity: 2));
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        _ = await NextAsync(observer);
        _ = await NextAsync(observer);
        await simulator.SendAsync("identity none");
        var before = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;

        await simulator.SendAsync("malformed");
        var unavailable = Assert.IsInstanceOfType<TelemetryUnavailable>(
            await NextAsync(observer));
        Assert.AreEqual(IracingErrorCodes.InvalidTelemetryFrame, unavailable.Error.Code);

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        Assert.IsInstanceOfType<TelemetryDisconnected>(await NextAsync(observer));

        await simulator.SendAsync("repair");
        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        var reconnected = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreNotEqual(before.Session.SessionKey, reconnected.Session.SessionKey);

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-SES-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task NoDataAfterInvalidReopenUsesLogicalConnectionStaleDeadline()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var timeProvider = new RecoveryDeadlineTimeProvider(ObservedAt);
        using var scopedFrameCopied = new ManualResetEventSlim(initialState: false);
        using var releaseScopedFrame = new ManualResetEventSlim(initialState: false);
        var copiedFrameCount = 0;
        var source = IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            timeProvider,
            FastOptions(eventBufferCapacity: 2),
            stableFrameCopied: () =>
            {
                if (Interlocked.Increment(ref copiedFrameCount) == 2)
                {
                    scopedFrameCopied.Set();
                    if (!releaseScopedFrame.Wait(EventTimeout))
                    {
                        throw new TimeoutException(
                            "The test did not release the scoped-frame observation.");
                    }
                }
            });
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        try
        {
            _ = await NextAsync(observer);
            _ = await NextAsync(observer);
            await simulator.SendAsync("identity none");
            var scoped = Assert.IsInstanceOfType<TelemetrySampleObserved>(
                await NextAsync(observer)).Sample;
            Assert.AreEqual(
                SimulatorIdentityScope.ConnectionScoped,
                scoped.Session.IdentityScope);
            Assert.IsTrue(scopedFrameCopied.Wait(EventTimeout));

            timeProvider.Advance(TimeSpan.FromSeconds(29));
            timeProvider.ArmInvalidRecovery();
            await simulator.SendAsync("malformed");
            releaseScopedFrame.Set();

            var unavailable = Assert.IsInstanceOfType<TelemetryUnavailable>(
                await NextAsync(observer));
            Assert.AreEqual(IracingErrorCodes.InvalidTelemetryFrame, unavailable.Error.Code);
            Assert.IsTrue(timeProvider.WaitForInvalidDeadlineCheck(EventTimeout));
            Assert.IsTrue(timeProvider.WaitForReopenTimestampCapture(EventTimeout));

            await simulator.SendAsync("repair-torn");
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            timeProvider.ReleaseReopenTimestamp();

            Assert.IsInstanceOfType<TelemetryDisconnected>(await NextAsync(observer));
        }
        finally
        {
            releaseScopedFrame.Set();
            timeProvider.ReleaseReopenTimestamp();
        }

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedMemoryProbeRejectsTornCopyUntilStableTickArrives()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var probe = IracingTestingRegistration.CreateFrameProbe(
            simulator.MemoryMapName,
            simulator.EventName);

        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        await simulator.SendAsync("torn");
        Assert.AreEqual(IracingTestFrameOutcome.NoData, probe.Read());

        await simulator.SendAsync("publish 11 40.1259");
        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedMemoryProbeResynchronizesAfterOfficialTickRegression()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var probe = IracingTestingRegistration.CreateFrameProbe(
            simulator.MemoryMapName,
            simulator.EventName);

        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        await simulator.SendAsync("tick-regression");
        Assert.AreEqual(IracingTestFrameOutcome.NoData, probe.Read());

        await simulator.SendAsync("publish 6 13.5");
        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedMemoryProbeCachesStableMetadataUntilItsVersionChanges()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var probe = IracingTestingRegistration.CreateFrameProbe(
            simulator.MemoryMapName,
            simulator.EventName);

        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        Assert.AreEqual(1, probe.VariableMetadataDecodeCount);
        Assert.AreEqual(1, probe.SessionInformationDecodeCount);

        await simulator.SendAsync("publish 6 13.5");
        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        Assert.AreEqual(1, probe.VariableMetadataDecodeCount);
        Assert.AreEqual(1, probe.SessionInformationDecodeCount);

        await simulator.SendAsync("identity 123456789");
        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        Assert.AreEqual(
            2,
            probe.VariableMetadataDecodeCount,
            "Session-information version is the SDK's nearest metadata-table discriminator.");
        Assert.AreEqual(2, probe.SessionInformationDecodeCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedMemoryProbeDoesNotCacheSessionUpdateThatRacesItsCopy()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var mutateDuringCopy = 0;
        using var probe = IracingTestingRegistration.CreateFrameProbe(
            simulator.MemoryMapName,
            simulator.EventName,
            betweenSessionInformationCopies: () =>
            {
                if (Volatile.Read(ref mutateDuringCopy) != 0)
                {
                    simulator.SendAsync("mutate-session").GetAwaiter().GetResult();
                }
            });

        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        Assert.AreEqual(1, probe.VariableMetadataDecodeCount);
        Assert.AreEqual(1, probe.SessionInformationDecodeCount);

        await simulator.SendAsync("identity-only 7654321");
        await simulator.SendAsync("publish 6 13.5");
        Volatile.Write(ref mutateDuringCopy, 1);
        Assert.AreEqual(IracingTestFrameOutcome.NoData, probe.Read());
        Assert.AreEqual(1, probe.VariableMetadataDecodeCount);
        Assert.AreEqual(1, probe.SessionInformationDecodeCount);

        Volatile.Write(ref mutateDuringCopy, 0);
        await simulator.SendAsync("identity 7654321");
        Assert.AreEqual(IracingTestFrameOutcome.Snapshot, probe.Read());
        Assert.AreEqual(2, probe.VariableMetadataDecodeCount);
        Assert.AreEqual(2, probe.SessionInformationDecodeCount);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SharedMemoryProbeTimesOutWhenFirstFrameRemainsTorn()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        await simulator.SendAsync("torn");
        var timeProvider = new ManualTimeProvider(ObservedAt);
        using var probe = IracingTestingRegistration.CreateFrameProbe(
            simulator.MemoryMapName,
            simulator.EventName,
            timeProvider);

        Assert.AreEqual(IracingTestFrameOutcome.NoData, probe.Read());
        timeProvider.Advance(TimeSpan.FromSeconds(31));

        Assert.AreEqual(IracingTestFrameOutcome.Disconnected, probe.Read());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncCallerCancellationInterruptsEventWait()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var source = CreateSource(simulator);
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        _ = await NextAsync(observer);
        _ = await NextAsync(observer);
        var pendingMove = observer.MoveNextAsync().AsTask();
        cancellationSource.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => pendingMove.WaitAsync(EventTimeout));
        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncDisposalEndsPendingObservationWithoutAnotherFrame()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var source = CreateSource(simulator);
        await using var observer = source.ObserveAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        _ = await NextAsync(observer);
        _ = await NextAsync(observer);
        var pendingMove = observer.MoveNextAsync().AsTask();
        await ((IAsyncDisposable)source).DisposeAsync();

        Assert.IsFalse(await pendingMove.WaitAsync(EventTimeout));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncDisconnectsAfterOfficialNoValidFrameTimeout()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var timeProvider = new ManualTimeProvider(ObservedAt);
        var source = IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            timeProvider,
            FastOptions());
        await using var observer = source.ObserveAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        _ = await NextAsync(observer);
        _ = await NextAsync(observer);
        var pendingMove = observer.MoveNextAsync().AsTask();
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        await simulator.SendAsync("torn");

        Assert.IsTrue(await pendingMove.WaitAsync(EventTimeout));
        Assert.IsInstanceOfType<TelemetryDisconnected>(observer.Current);
        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-001")]
    [TestProperty("Requirement", "IR-INC-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncProducerPreservesResetAndRiseWhileConsumerIsPaused()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var copiedFrames = new SemaphoreSlim(initialCount: 0);
        var source = IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            new FixedTimeProvider(ObservedAt),
            FastOptions(eventBufferCapacity: 8),
            () => copiedFrames.Release());
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));

        await simulator.SendAsync("publish 4 12.9");
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));
        await simulator.SendAsync("publish 0 13.0");
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));
        await simulator.SendAsync("publish 2 14.0");
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));

        var initial = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        var reset = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        var rise = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(4, initial.IncidentCounter.Value);
        Assert.AreEqual(0, reset.IncidentCounter.Value);
        Assert.AreEqual(2, rise.IncidentCounter.Value);

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task ObserveAsyncReportsOverflowInsteadOfDroppingMeaningfulTransition()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        using var copiedFrames = new SemaphoreSlim(initialCount: 0);
        var source = IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            new FixedTimeProvider(ObservedAt),
            FastOptions(eventBufferCapacity: 2),
            () => copiedFrames.Release());
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        Assert.IsInstanceOfType<TelemetryConnected>(await NextAsync(observer));
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));

        await simulator.SendAsync("publish 0 13.0");
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));
        await simulator.SendAsync("publish 2 14.0");
        Assert.IsTrue(await copiedFrames.WaitAsync(EventTimeout));

        var initial = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        var reset = Assert.IsInstanceOfType<TelemetrySampleObserved>(
            await NextAsync(observer)).Sample;
        Assert.AreEqual(4, initial.IncidentCounter.Value);
        Assert.AreEqual(0, reset.IncidentCounter.Value);
        var unavailable = Assert.IsInstanceOfType<TelemetryUnavailable>(
            await NextAsync(observer));
        Assert.AreEqual(IracingErrorCodes.TelemetryBufferOverflow, unavailable.Error.Code);
        Assert.IsFalse(await observer.MoveNextAsync().AsTask().WaitAsync(EventTimeout));

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    public async Task ObserveAsyncMissingEndpointReportsOneStableUnavailableError()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var source = IracingTestingRegistration.CreateTelemetrySource(
            $"Local\\MissingMap_{suffix}",
            $"Local\\MissingEvent_{suffix}",
            new FixedTimeProvider(ObservedAt),
            FastOptions());
        using var cancellationSource = new CancellationTokenSource(EventTimeout);
        await using var observer = source.ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();

        var unavailable = Assert.IsInstanceOfType<TelemetryUnavailable>(
            await NextAsync(observer));
        Assert.AreEqual(IracingErrorCodes.TelemetryUnavailable, unavailable.Error.Code);

        await ((IAsyncDisposable)source).DisposeAsync();
    }

    private static ITelemetrySource CreateSource(SimulatorProcess simulator) =>
        IracingTestingRegistration.CreateTelemetrySource(
            simulator.MemoryMapName,
            simulator.EventName,
            new FixedTimeProvider(ObservedAt),
            FastOptions());

    private static IracingOptions FastOptions(int eventBufferCapacity = 256) =>
        IracingOptions.TryCreate(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(25),
            eventBufferCapacity).Value;

    private static async Task<TelemetryEvent> NextAsync(
        IAsyncEnumerator<TelemetryEvent> observer)
    {
        Assert.IsTrue(await observer.MoveNextAsync().AsTask().WaitAsync(EventTimeout));
        return observer.Current;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _value;

        public FixedTimeProvider(DateTimeOffset value)
        {
            _value = value;
        }

        public override DateTimeOffset GetUtcNow() => _value;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            _utcNow.AddTicks(Volatile.Read(ref _timestamp));

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);
    }

    private sealed class RecoveryDeadlineTimeProvider : TimeProvider, IDisposable
    {
        private readonly DateTimeOffset _utcNow;
        private readonly ManualResetEventSlim _invalidDeadlineChecked = new(initialState: false);
        private readonly ManualResetEventSlim _reopenTimestampCaptured = new(initialState: false);
        private readonly ManualResetEventSlim _releaseReopenTimestamp = new(initialState: false);
        private long _timestamp;
        private int _isArmed;
        private int _armedTimestampReadCount;

        public RecoveryDeadlineTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            _utcNow.AddTicks(Volatile.Read(ref _timestamp));

        public override long GetTimestamp()
        {
            var capturedTimestamp = Volatile.Read(ref _timestamp);
            if (Volatile.Read(ref _isArmed) == 0)
            {
                return capturedTimestamp;
            }

            var readCount = Interlocked.Increment(ref _armedTimestampReadCount);
            if (readCount == 1)
            {
                _invalidDeadlineChecked.Set();
            }
            else if (readCount == 2)
            {
                Volatile.Write(ref _isArmed, 0);
                _reopenTimestampCaptured.Set();
                if (!_releaseReopenTimestamp.Wait(EventTimeout))
                {
                    throw new TimeoutException(
                        "The test did not release the reopened connection timestamp.");
                }
            }

            return capturedTimestamp;
        }

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);

        public void ArmInvalidRecovery()
        {
            Interlocked.Exchange(ref _armedTimestampReadCount, 0);
            Volatile.Write(ref _isArmed, 1);
        }

        public bool WaitForInvalidDeadlineCheck(TimeSpan timeout) =>
            _invalidDeadlineChecked.Wait(timeout);

        public bool WaitForReopenTimestampCapture(TimeSpan timeout) =>
            _reopenTimestampCaptured.Wait(timeout);

        public void ReleaseReopenTimestamp() => _releaseReopenTimestamp.Set();

        public void Dispose()
        {
            _invalidDeadlineChecked.Dispose();
            _reopenTimestampCaptured.Dispose();
            _releaseReopenTimestamp.Dispose();
        }
    }
}
