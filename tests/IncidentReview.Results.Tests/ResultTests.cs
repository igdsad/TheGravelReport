using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Results.Tests;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void SuccessHasOnlySuccessState()
    {
        var result = Result.Success();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void FailureHasOnlyFailureState()
    {
        var error = CreateError();

        var result = Result.Failure(error);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreSame(error, result.Error);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void FailureRejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Result.Failure(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ErrorKindContainsOnlyExpectedOperationalCategories()
    {
        var expected = new[]
        {
            ErrorKind.Validation,
            ErrorKind.NotFound,
            ErrorKind.Conflict,
            ErrorKind.Unavailable,
            ErrorKind.Persistence,
            ErrorKind.Integration,
            ErrorKind.Indeterminate,
        };

        CollectionAssert.AreEqual(expected, Enum.GetValues<ErrorKind>());
    }

    private static Error CreateError() => Error.Create(
        ErrorCode.Define("store.sqlite.busy"),
        ErrorKind.Persistence,
        "The data store is busy.");
}
