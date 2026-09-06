using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

internal static class DomainTestAssertions
{
    public static void IsValidationFailure<T>(Result<T> result, string expectedCode)
        where T : notnull
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
        Assert.AreEqual(expectedCode, result.Error.Code.Value);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
    }
}
