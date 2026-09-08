using IncidentReview.Domain;
using IncidentReview.Iracing.Testing;
using IncidentReview.Replay.Contracts;

namespace IncidentReview.Iracing.Tests;

[TestClass]
public sealed class ReplayControllerTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SeekEncodesOfficialGoldenVector()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var result = await context.Controller.SeekAsync(
            Position(sessionNumber: 2, milliseconds: 12_345),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, context.Messages);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 12,
                WParam: 12 | (2 << 16),
                LParam: 12_345),
            context.Messages[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task SeekRejectsValuesOutsideVerifiedWireSlots()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var sessionOverflow = await context.Controller.SeekAsync(
            Position(short.MaxValue + 1, 0),
            CancellationToken.None);
        var timeOverflow = await context.Controller.SeekAsync(
            Position(0, (long)int.MaxValue + 1),
            CancellationToken.None);

        Assert.IsFalse(sessionOverflow.IsSuccess);
        Assert.AreEqual(
            IracingErrorCodes.UnsupportedReplayPosition,
            sessionOverflow.Error?.Code);
        Assert.IsFalse(timeOverflow.IsSuccess);
        Assert.AreEqual(
            IracingErrorCodes.UnsupportedReplayPosition,
            timeOverflow.Error?.Code);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(4d, 4, 0)]
    [DataRow(0.25d, 4, 1)]
    [DataRow(1d, 1, 0)]
    public async Task SetPlaybackEncodesExactlyRepresentableRates(
        double rate,
        int wireSpeed,
        int slowMotion)
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var result = await context.Controller.SetPlaybackAsync(
            ReplayPlayback.TryCreatePlaying(rate).Value,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, context.Messages);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 3,
                WParam: 3 | (wireSpeed << 16),
                LParam: slowMotion),
            context.Messages[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    public async Task SetPlaybackEncodesPauseAsZeroSpeed()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = await context.Controller.SetPlaybackAsync(
            ReplayPlayback.Paused,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, context.Messages);
        Assert.AreEqual(
            new IracingTestReplayMessage(Command: 3, WParam: 3, LParam: 0),
            context.Messages[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [DataRow(1.5d)]
    [DataRow(0.75d)]
    [DataRow(0.3d)]
    [DataRow(32768d)]
    [DataRow(0.00001d)]
    public async Task SetPlaybackRejectsRatesThatRequireRounding(double rate)
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = await context.Controller.SetPlaybackAsync(
            ReplayPlayback.TryCreatePlaying(rate).Value,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.UnsupportedPlaybackRate, result.Error?.Code);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public void ValidatePlaybackRejectsUnsupportedRateWithoutBroadcast()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var playback = ReplayPlayback.TryCreatePlaying(0.75d).Value;

        var result = context.Controller.ValidatePlayback(playback);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.UnsupportedPlaybackRate, result.Error?.Code);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    public void ValidatePlaybackAcceptsRepresentableRateWithoutBroadcast()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var playback = ReplayPlayback.TryCreatePlaying(0.25d).Value;

        var result = context.Controller.ValidatePlayback(playback);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [DataRow(IracingTestDeliveryMode.EndpointUnavailable, "iracing.replay.unavailable")]
    [DataRow(IracingTestDeliveryMode.DeliveryRejected, "iracing.replay.delivery-failed")]
    public async Task ReplayCommandsTranslateDeliveryFailures(
        IracingTestDeliveryMode deliveryMode,
        string expectedCode)
    {
        var context = IracingTestingRegistration.CreateReplayContext(deliveryMode);

        var result = await context.Controller.SeekAsync(Position(0, 0), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.Error?.Code.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReplayCommandsRejectDisconnectedTelemetryBeforeBroadcast()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            telemetryAvailable: false);

        var result = await context.Controller.SeekAsync(Position(0, 0), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplayUnavailable, result.Error?.Code);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task LaterValidFrameAtomicallyRestoresReplayCommands()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            telemetryAvailable: false);
        context.ObserveFrame(State());

        var result = await context.Controller.SeekAsync(
            Position(0, 0),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public async Task ReplayCommandsPropagateCancellationBeforeDelivery()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await context.Controller.SeekAsync(
                Position(0, 0),
                cancellationSource.Token));
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task SeekRequiresALaterConfirmingFrameWithinTolerance()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromSeconds(1));

        var pending = context.Controller.SeekAsync(
            Position(2, 12_345),
            CancellationToken.None).AsTask();
        Assert.HasCount(1, context.Messages);

        context.ObserveFrame(State(
            replaySessionNumber: 2,
            replayMilliseconds: 12_545));

        var result = await pending;
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task SeekReportsExactTimeoutWhenLaterFramesDoNotReachTarget()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromMilliseconds(25));

        var result = await context.Controller.SeekAsync(
            Position(0, 0),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplaySeekTimeout, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusPlayerEncodesOfficialCameraSwitchNumberGoldenVector()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = await context.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, context.Messages);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 1,
                WParam: 1 | (23 << 16),
                LParam: 2 | (20 << 16)),
            context.Messages[0]);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusParticipantResolvesExactCanonicalOpponentReplayMetadata()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: OpponentReplaySessionInfo);
        context.ObserveFrame(State(sessionInfo: OpponentReplaySessionInfo));
        var opponent = IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate("car-index:2:team:22").Value,
            "Stored Driver",
            "Stored Team",
            "display-only").Value;

        var result = await context.Controller.FocusParticipantAsync(
            opponent,
            "TV1",
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 1,
                WParam: 1 | (12 << 16),
                LParam: 2 | (20 << 16)),
            context.Messages.Single());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-INC-007")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusParticipantUsesStableTeamCarForDriverScopedCounterIdentity()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: OpponentReplaySessionInfo);
        context.ObserveFrame(State(sessionInfo: OpponentReplaySessionInfo));
        var opponent = IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate(
                "car-index:2:team:22:driver-user:999").Value,
            "Earlier Team Driver",
            "Stored Team",
            "display-only").Value;

        var result = await context.Controller.FocusParticipantAsync(
            opponent,
            "TV1",
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 1,
                WParam: 1 | (12 << 16),
                LParam: 2 | (20 << 16)),
            context.Messages.Single());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusParticipantRejectsPriorEntrantAfterExplicitIdentityChange()
    {
        var currentSessionInfo = OpponentReplaySessionInfo.Replace(
            "TeamID: 22",
            "TeamID: 23",
            StringComparison.Ordinal).Replace(
                "CurrentSessionNum: 0",
                "CurrentSessionNum: 1",
                StringComparison.Ordinal);
        var context = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: currentSessionInfo);
        context.ObserveFrame(State(
            replaySessionNumber: 1,
            sessionInfo: currentSessionInfo,
            liveSessionNumber: 1));
        var prior = Participant("car-index:2:team:22");
        var current = Participant("car-index:2:team:23");

        var rejected = await context.Controller.FocusParticipantAsync(
            prior,
            "TV1",
            CancellationToken.None);
        var focused = await context.Controller.FocusParticipantAsync(
            current,
            "TV1",
            CancellationToken.None);

        Assert.IsFalse(rejected.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplayMetadataUnavailable, rejected.Error?.Code);
        Assert.IsTrue(focused.IsSuccess);
        Assert.HasCount(1, context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusParticipantAllowsAnotherReplayHeatButRejectsStaleLiveMetadata()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: OpponentReplaySessionInfo);
        var opponent = Participant("car-index:2:team:22");

        context.ObserveFrame(State(
            replaySessionNumber: 0,
            sessionInfo: OpponentReplaySessionInfo,
            liveSessionNumber: 1));
        var liveMismatch = await context.Controller.FocusParticipantAsync(
            opponent,
            "TV1",
            CancellationToken.None);

        context.ObserveFrame(State(
            replaySessionNumber: 1,
            sessionInfo: OpponentReplaySessionInfo,
            liveSessionNumber: 0));
        var earlierHeat = await context.Controller.FocusParticipantAsync(
            opponent,
            "TV1",
            CancellationToken.None);

        Assert.AreEqual(
            IracingErrorCodes.ReplayMetadataUnavailable,
            liveMismatch.Error?.Code);
        Assert.IsTrue(earlierHeat.IsSuccess);
        Assert.HasCount(1, context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    public async Task FocusPlayerWithoutPreferencePreservesCurrentCamera()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = await context.Controller.FocusParticipantAsync(
            LocalPlayer(),
            preferredCamera: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            new IracingTestReplayMessage(
                Command: 1,
                WParam: 1 | (23 << 16),
                LParam: 1 | (10 << 16)),
            context.Messages.Single());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    public async Task FocusPlayerAcceptsGroupConfirmationAfterAutoShotAdvancesCamera()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromSeconds(1));

        var pending = context.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None).AsTask();
        context.ObserveFrame(State(
            cameraCarIndex: 4,
            cameraGroupNumber: 2,
            cameraNumber: 21));

        Assert.IsTrue((await pending).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public async Task FocusParticipantDoesNotConfirmAfterTargetIdentityChanges()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromMilliseconds(25),
            sessionInfo: OpponentReplaySessionInfo);
        context.ObserveFrame(State(sessionInfo: OpponentReplaySessionInfo));
        var pending = context.Controller.FocusParticipantAsync(
            Participant("car-index:2:team:22"),
            "TV1",
            CancellationToken.None).AsTask();
        Assert.HasCount(1, context.Messages);

        var replacementSessionInfo = OpponentReplaySessionInfo.Replace(
            "TeamID: 22",
            "TeamID: 23",
            StringComparison.Ordinal);
        context.ObserveFrame(State(
            cameraCarIndex: 2,
            cameraGroupNumber: 2,
            cameraNumber: 20,
            sessionInfo: replacementSessionInfo));

        var result = await pending;
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplayCameraTimeout, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task FocusPlayerReturnsExactMetadataAndCameraErrorsWithoutBroadcast()
    {
        var missingMetadata = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: "WeekendInfo:\n SubSessionID: 1");
        var missingCamera = IracingTestingRegistration.CreateReplayContext();

        var metadataResult = await missingMetadata.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None);
        var cameraResult = await missingCamera.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "Not Installed",
            CancellationToken.None);

        Assert.AreEqual(
            IracingErrorCodes.ReplayMetadataUnavailable,
            metadataResult.Error?.Code);
        Assert.AreEqual(IracingErrorCodes.ReplayCameraNotFound, cameraResult.Error?.Code);
        Assert.IsEmpty(missingMetadata.Messages);
        Assert.IsEmpty(missingCamera.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task PlaybackReportsItsOwnConfirmationTimeout()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromMilliseconds(25));

        var result = await context.Controller.SetPlaybackAsync(
            ReplayPlayback.Paused,
            CancellationToken.None);

        Assert.AreEqual(IracingErrorCodes.ReplayPlaybackTimeout, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CameraReportsItsOwnConfirmationTimeout()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            autoApplyCommands: false,
            confirmationTimeout: TimeSpan.FromMilliseconds(25));

        var result = await context.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None);

        Assert.AreEqual(IracingErrorCodes.ReplayCameraTimeout, result.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-CON-001")]
    public async Task CoalescedTelemetryFrameStillConfirmsSeek()
    {
        using var simulator = await SimulatorProcess.StartAsync();
        var context = IracingTestingRegistration.CreateIntegrationContext(
            simulator.MemoryMapName,
            simulator.EventName,
            confirmationTimeout: TimeSpan.FromSeconds(2));
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = context.Telemetry
            .ObserveAsync(cancellationSource.Token)
            .GetAsyncEnumerator();
        Assert.IsTrue(await observer.MoveNextAsync());
        Assert.IsTrue(await observer.MoveNextAsync());

        await simulator.SendAsync("replay 5 22.2229 8");
        Assert.IsTrue(await observer.MoveNextAsync());
        var pending = context.Controller.SeekAsync(
            Position(5, 30_000),
            cancellationSource.Token).AsTask();
        Assert.HasCount(1, context.Messages);

        await simulator.SendAsync("replay 5 30.1000 8");

        Assert.IsTrue((await pending).IsSuccess);
        await ((IAsyncDisposable)context.Telemetry).DisposeAsync();
    }

    private static IracingTestReplayState State(
        int replaySessionNumber = 0,
        long replayMilliseconds = 0,
        int replayPlaySpeed = 1,
        bool replayPlaySlowMotion = false,
        int cameraState = 1,
        int cameraCarIndex = 4,
        int cameraGroupNumber = 1,
        int cameraNumber = 10,
        string? sessionInfo = null,
        int liveSessionNumber = 0) => new(
        replaySessionNumber,
        replayMilliseconds,
        replayPlaySpeed,
        replayPlaySlowMotion,
        cameraState,
        cameraCarIndex,
        cameraGroupNumber,
        cameraNumber,
        sessionInfo ?? ReplaySessionInfo,
        liveSessionNumber);

    private const string ReplaySessionInfo = """
        SessionInfo:
         CurrentSessionNum: 0
        DriverInfo:
         DriverCarIdx: 4
         Drivers:
         - CarIdx: 4
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 1
           GroupName: Cockpit
           Cameras:
           - CameraNum: 10
         - GroupNum: 2
           GroupName: TV1
           Cameras:
           - CameraNum: 20
           - CameraNum: 21
        """;

    private const string OpponentReplaySessionInfo = """
        SessionInfo:
         CurrentSessionNum: 0
        DriverInfo:
         DriverCarIdx: 4
         PaceCarIdx: 0
         Drivers:
         - CarIdx: 2
           UserName: Opponent Driver
           TeamID: 22
           UserID: 222
           CarNumber: "012"
           CarNumberRaw: 12
         - CarIdx: 4
           UserName: Local Driver
           TeamID: 0
           UserID: 444
           CarNumber: "023"
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 1
           GroupName: Cockpit
           Cameras:
           - CameraNum: 10
         - GroupNum: 2
           GroupName: TV1
           Cameras:
           - CameraNum: 20
        """;

    private static ReplayPosition Position(int sessionNumber, long milliseconds) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionTime.TryCreateMilliseconds(milliseconds).Value).Value;

    private static IncidentParticipant LocalPlayer() =>
        IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate("local-player").Value,
            driverName: null,
            teamName: null,
            carNumber: null).Value;

    private static IncidentParticipant Participant(string identity) =>
        IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate(identity).Value,
            driverName: null,
            teamName: null,
            carNumber: null).Value;
}
