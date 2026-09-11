using IncidentReview.EventSync.Sqlite.Options;

namespace IncidentReview.EventSync.Sqlite.Tests;

[TestClass]
public sealed class SqliteEventInboxOptionsTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public void ValidOptionsRetainBoundedExecutorAndAbsolutePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "GravelReview.Tests", "events.db");

        var result = SqliteEventInboxOptions.TryCreate(
            path,
            busyTimeoutSeconds: 17,
            executorCapacity: 37);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Path.GetFullPath(path), result.Value.DatabasePath);
        Assert.AreEqual(17, result.Value.BusyTimeoutSeconds);
        Assert.AreEqual(37, result.Value.ExecutorCapacity);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    [DataRow("relative-path")]
    [DataRow("zero-timeout")]
    [DataRow("excess-timeout")]
    [DataRow("zero-capacity")]
    [DataRow("excess-capacity")]
    public void EachInvalidConfigurationDimensionReturnsSafeValidationFailure(string dimension)
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "GravelReview.Tests", "events.db");
        var path = dimension == "relative-path" ? "events.db" : absolutePath;
        var timeout = dimension switch
        {
            "zero-timeout" => 0,
            "excess-timeout" => 61,
            _ => 5,
        };
        var capacity = dimension switch
        {
            "zero-capacity" => 0,
            "excess-capacity" => 4097,
            _ => 256,
        };

        var result = SqliteEventInboxOptions.TryCreate(path, timeout, capacity);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SqliteEventInboxErrorCodes.InvalidOptions, result.Error?.Code);
    }
}
