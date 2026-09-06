using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Results.Tests;

[TestClass]
public sealed class ErrorTests
{
    private static readonly ErrorCode BusyCode = ErrorCode.Define("store.sqlite.busy");

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateExposesTheValidatedErrorState()
    {
        var error = Error.Create(BusyCode, ErrorKind.Persistence, "The data store is busy.");

        Assert.AreEqual(BusyCode, error.Code);
        Assert.AreEqual(ErrorKind.Persistence, error.Kind);
        Assert.AreEqual("The data store is busy.", error.Message);
        Assert.AreEqual("store.sqlite.busy: The data store is busy.", error.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreatePreservesMessageWhitespaceWithoutSilentlyRewritingIt()
    {
        var error = Error.Create(BusyCode, ErrorKind.Persistence, " Store busy. ");

        Assert.AreEqual(" Store busy. ", error.Message);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateRejectsADefaultCode()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => Error.Create(default, ErrorKind.Persistence, "The data store is busy."));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateRejectsAnUndefinedKind()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Error.Create(BusyCode, (ErrorKind)int.MaxValue, "The data store is busy."));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateRejectsANullMessage()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => Error.Create(BusyCode, ErrorKind.Persistence, null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t\r\n")]
    public void CreateRejectsABlankMessage(string message)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => Error.Create(BusyCode, ErrorKind.Persistence, message));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ErrorsWithEqualStateHaveValueEquality()
    {
        var first = Error.Create(BusyCode, ErrorKind.Persistence, "The data store is busy.");
        var second = Error.Create(BusyCode, ErrorKind.Persistence, "The data store is busy.");

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.IsFalse(first != second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ErrorsWithDifferentStateAreNotEqual()
    {
        var baseline = Error.Create(BusyCode, ErrorKind.Persistence, "The data store is busy.");
        var otherCode = Error.Create(
            ErrorCode.Define("store.sqlite.locked"),
            ErrorKind.Persistence,
            "The data store is busy.");
        var otherKind = Error.Create(BusyCode, ErrorKind.Unavailable, "The data store is busy.");
        var otherMessage = Error.Create(BusyCode, ErrorKind.Persistence, "Try again later.");

        Assert.AreNotEqual(baseline, otherCode);
        Assert.AreNotEqual(baseline, otherKind);
        Assert.AreNotEqual(baseline, otherMessage);
        Assert.IsFalse(baseline.Equals(null));
        Assert.IsTrue(baseline != otherCode);
    }
}
