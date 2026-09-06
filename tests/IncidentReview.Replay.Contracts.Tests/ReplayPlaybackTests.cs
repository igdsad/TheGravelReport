namespace IncidentReview.Replay.Contracts.Tests;

[TestClass]
public sealed class ReplayPlaybackTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    public void PausedCarriesNoMeaninglessPlaybackRate()
    {
        var paused = ReplayPlayback.Paused;

        Assert.IsTrue(paused.IsPaused);
        Assert.IsNull(paused.PlaybackRate);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(double.Epsilon)]
    [DataRow(0.125)]
    [DataRow(1.0)]
    [DataRow(16.0)]
    [DataRow(double.MaxValue)]
    public void TryCreatePlayingPreservesEveryFinitePositiveRate(double playbackRate)
    {
        var result = ReplayPlayback.TryCreatePlaying(playbackRate);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value.IsPaused);
        Assert.AreEqual(playbackRate, result.Value.PlaybackRate);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0.0)]
    [DataRow(-double.Epsilon)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void TryCreatePlayingRejectsEveryNonPositiveOrNonFiniteClass(double playbackRate)
    {
        var result = ReplayPlayback.TryCreatePlaying(playbackRate);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(ReplayErrorCodes.InvalidPlaybackRate, result.Error.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void PlaybackCannotBeConstructedOrMutatedIntoAnInvalidState()
    {
        Assert.IsTrue(typeof(ReplayPlayback).IsSealed);
        Assert.IsEmpty(typeof(ReplayPlayback).GetConstructors());
        Assert.IsTrue(typeof(ReplayPlayback).GetProperties().All(property =>
            property.SetMethod is null));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void EqualPlayingIntentsHaveValueEquality()
    {
        var first = ReplayPlayback.TryCreatePlaying(0.5).Value;
        var second = ReplayPlayback.TryCreatePlaying(0.5).Value;

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, ReplayPlayback.Paused);
    }
}
