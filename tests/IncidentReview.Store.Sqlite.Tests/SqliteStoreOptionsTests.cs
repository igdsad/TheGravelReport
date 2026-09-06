using IncidentReview.Store.Sqlite.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.Sqlite.Tests;

[TestClass]
public sealed class SqliteStoreOptionsTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void AbsoluteLocalPathAndFiniteTimeoutAreAccepted()
    {
        var path = Path.Combine(Path.GetTempPath(), "IncidentReview.Tests", "store.db");

        var result = SqliteStoreOptions.TryCreate(
            path,
            busyTimeoutSeconds: 7,
            executorCapacity: 8);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Path.GetFullPath(path), result.Value.DatabasePath);
        Assert.AreEqual(7, result.Value.BusyTimeoutSeconds);
        Assert.AreEqual(8, result.Value.ExecutorCapacity);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow(null, 5)]
    [DataRow("", 5)]
    [DataRow("relative.db", 5)]
    [DataRow("\\\\server\\share\\store.db", 5)]
    [DataRow("C:\\store.db", 0)]
    [DataRow("C:\\store.db", 61)]
    public void InvalidOptionsReturnStableSafeFailure(string? path, int timeout)
    {
        var result = SqliteStoreOptions.TryCreate(path, timeout);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("store.sqlite.options-invalid", result.Error?.Code.ToString());
        Assert.AreEqual("The SQLite store configuration is invalid.", result.Error?.Message);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow(0)]
    [DataRow(1025)]
    public void InvalidExecutorCapacityReturnsStableSafeFailure(int capacity)
    {
        var path = Path.Combine(Path.GetTempPath(), "IncidentReview.Tests", "store.db");

        var result = SqliteStoreOptions.TryCreate(path, executorCapacity: capacity);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("store.sqlite.options-invalid", result.Error?.Code.ToString());
    }
}
