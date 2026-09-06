using IncidentReview.Results;

namespace IncidentReview.Domain;

internal static class DomainValidationError
{
    public static Error Create(string code, string message) => Error.Create(
        ErrorCode.Define(code),
        ErrorKind.Validation,
        message);
}
