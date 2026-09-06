using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Results.Tests;

[TestClass]
public sealed class ErrorCodeTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("store.sqlite.busy")]
    [DataRow("store.commit.indeterminate")]
    [DataRow("iracing2.replay-v2.unavailable3")]
    public void DefineAcceptsDocumentedNamespacedSyntax(string value)
    {
        var code = ErrorCode.Define(value);

        Assert.AreEqual(value, code.Value);
        Assert.AreEqual(value, code.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void DefineAcceptsACodeAtTheMaximumLength()
    {
        var value = $"a.{new string('b', 126)}";

        var code = ErrorCode.Define(value);

        Assert.AreEqual(128, code.Value.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void DefineRejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ErrorCode.Define(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("")]
    [DataRow("store")]
    [DataRow("Store.sqlite")]
    [DataRow("store.SQLite")]
    [DataRow("store_sqlite.busy")]
    [DataRow("store/sqlite/busy")]
    [DataRow(" store.sqlite.busy")]
    [DataRow("store.sqlite.busy ")]
    [DataRow(".store.sqlite")]
    [DataRow("store.sqlite.")]
    [DataRow("store..sqlite")]
    [DataRow("1store.sqlite")]
    [DataRow("store.1sqlite")]
    [DataRow("store.-sqlite")]
    [DataRow("store.sqlite-")]
    [DataRow("store.sql--ite")]
    [DataRow("störe.sqlite")]
    public void DefineRejectsMalformedSyntax(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ErrorCode.Define(value));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void DefineRejectsACodeOverTheMaximumLength()
    {
        var value = $"a.{new string('b', 127)}";

        Assert.ThrowsExactly<ArgumentException>(() => ErrorCode.Define(value));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void EqualCodesHaveValueEqualityAndEqualHashCodes()
    {
        var first = ErrorCode.Define("store.sqlite.busy");
        var second = ErrorCode.Define("store.sqlite.busy");

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.IsFalse(first != second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void DifferentCodesAreNotEqual()
    {
        var first = ErrorCode.Define("store.sqlite.busy");
        var second = ErrorCode.Define("store.sqlite.locked");

        Assert.AreNotEqual(first, second);
        Assert.IsFalse(first == second);
        Assert.IsTrue(first != second);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void DefaultCodeDoesNotExposeAValue()
    {
        var code = default(ErrorCode);

        Assert.AreEqual(string.Empty, code.ToString());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = code.Value);
        Assert.AreNotEqual(ErrorCode.Define("store.sqlite.busy"), code);
    }
}
