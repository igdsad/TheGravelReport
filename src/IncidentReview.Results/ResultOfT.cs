namespace IncidentReview.Results;

/// <summary>
/// Represents either a non-null successful value or an expected failure.
/// </summary>
/// <typeparam name="T">The non-null success value type.</typeparam>
public sealed class Result<T>
    where T : notnull
{
    private readonly T _value;

    private Result(bool isSuccess, T value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    /// <summary>
    /// Gets whether the operation completed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the successful value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>
    /// Gets the expected error when the operation failed; otherwise <see langword="null"/>.
    /// </summary>
    public Error? Error { get; }

    /// <summary>
    /// Creates a successful result containing <paramref name="value"/>.
    /// </summary>
#pragma warning disable CA1000 // Factories keep valid-state construction on the Result<T> contract.
    public static Result<T> Success(T value)
#pragma warning restore CA1000
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(isSuccess: true, value, error: null);
    }

    /// <summary>
    /// Creates a failed result containing <paramref name="error"/>.
    /// </summary>
#pragma warning disable CA1000 // Factories keep valid-state construction on the Result<T> contract.
    public static Result<T> Failure(Error error)
#pragma warning restore CA1000
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(isSuccess: false, value: default!, error);
    }
}
