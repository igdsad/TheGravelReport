namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class ReplayOffsetTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [DataRow(-60_000L)]
    [DataRow(-2_000L)]
    [DataRow(0L)]
    [DataRow(2_000L)]
    [DataRow(60_000L)]
    public void CreatesSupportedSignedMillisecondOffsets(long milliseconds)
    {
        var result = ReplayOffset.TryCreateMilliseconds(milliseconds);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(milliseconds, result.Value.Milliseconds);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [DataRow(-60_001L)]
    [DataRow(60_001L)]
    public void RejectsOffsetsOutsideTheReviewWindow(long milliseconds)
    {
        Assert.IsFalse(ReplayOffset.TryCreateMilliseconds(milliseconds).IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    public void AppliesOffsetsAndClampsBeforeSessionStart()
    {
        var before = ReplayOffset.TryCreateMilliseconds(-2_000).Value;
        var exact = ReplayOffset.Zero;
        var after = ReplayOffset.TryCreateMilliseconds(2_000).Value;
        var position = SessionTime.TryCreateMilliseconds(1_000).Value;

        Assert.AreEqual(0, before.ApplyTo(position).Value.Milliseconds);
        Assert.AreEqual(1_000, exact.ApplyTo(position).Value.Milliseconds);
        Assert.AreEqual(3_000, after.ApplyTo(position).Value.Milliseconds);
    }
}
