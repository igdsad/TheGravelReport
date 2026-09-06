using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Results.Tests;

[TestClass]
public sealed class ResultOfTTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void SuccessHasOnlyTheSuppliedReferenceValue()
    {
        var value = new object();

        var result = Result<object>.Success(value);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(value, result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void SuccessSupportsNonNullValueTypes()
    {
        var result = Result<int>.Success(42);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(42, result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void SuccessRejectsANullReferenceValue()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void FailureHasOnlyTheSuppliedError()
    {
        var error = CreateError();

        var result = Result<string>.Failure(error);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(error, result.Error);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void FailureValueAccessThrowsInsteadOfExposingADefaultValue()
    {
        var result = Result<int>.Failure(CreateError());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
        Assert.AreEqual("A failed result has no value.", exception.Message);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void FailureRejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Result<string>.Failure(null!));
    }

    private static Error CreateError() => Error.Create(
        ErrorCode.Define("store.sqlite.busy"),
        ErrorKind.Persistence,
        "The data store is busy.");
}
