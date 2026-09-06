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
    public void SeekEncodesOfficialGoldenVector()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var result = context.Controller.Seek(
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
    public void SeekRejectsValuesOutsideVerifiedWireSlots()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var sessionOverflow = context.Controller.Seek(
            Position(short.MaxValue + 1, 0),
            CancellationToken.None);
        var timeOverflow = context.Controller.Seek(
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
    public void SetPlaybackEncodesExactlyRepresentableRates(
        double rate,
        int wireSpeed,
        int slowMotion)
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        var result = context.Controller.SetPlayback(
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
    public void SetPlaybackEncodesPauseAsZeroSpeed()
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = context.Controller.SetPlayback(
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
    public void SetPlaybackRejectsRatesThatRequireRounding(double rate)
    {
        var context = IracingTestingRegistration.CreateReplayContext();

        var result = context.Controller.SetPlayback(
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
    public void ReplayCommandsTranslateDeliveryFailures(
        IracingTestDeliveryMode deliveryMode,
        string expectedCode)
    {
        var context = IracingTestingRegistration.CreateReplayContext(deliveryMode);

        var result = context.Controller.Seek(Position(0, 0), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.Error?.Code.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public void ReplayCommandsRejectDisconnectedTelemetryBeforeBroadcast()
    {
        var context = IracingTestingRegistration.CreateReplayContext(
            telemetryAvailable: false);

        var result = context.Controller.Seek(Position(0, 0), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplayUnavailable, result.Error?.Code);
        Assert.IsEmpty(context.Messages);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public void ReplayCommandsPropagateCancellationBeforeDelivery()
    {
        var context = IracingTestingRegistration.CreateReplayContext();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => context.Controller.Seek(
                Position(0, 0),
                cancellationSource.Token));
        Assert.IsEmpty(context.Messages);
    }

    private static ReplayPosition Position(int sessionNumber, long milliseconds) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionTime.TryCreateMilliseconds(milliseconds).Value).Value;
}
