namespace IncidentReview.Results;

/// <summary>
/// Represents either successful completion or an expected failure.
/// </summary>
public sealed class Result
{
    private static readonly Result SuccessfulResult = new(isSuccess: true, error: null);

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Gets whether the operation completed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the expected error when the operation failed; otherwise <see langword="null"/>.
    /// </summary>
    public Error? Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => SuccessfulResult;

    /// <summary>
    /// Creates a failed result containing <paramref name="error"/>.
    /// </summary>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(isSuccess: false, error);
    }
}
