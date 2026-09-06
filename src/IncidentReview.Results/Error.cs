namespace IncidentReview.Results;

/// <summary>
/// Describes an expected failure without exposing provider exception details.
/// </summary>
#pragma warning disable CA1716 // Error is the repository's deliberate cross-boundary contract name.
public sealed class Error : IEquatable<Error>
#pragma warning restore CA1716
{
    private Error(ErrorCode code, ErrorKind kind, string message)
    {
        Code = code;
        Kind = kind;
        Message = message;
    }

    /// <summary>
    /// Gets the stable, machine-readable error code.
    /// </summary>
    public ErrorCode Code { get; }

    /// <summary>
    /// Gets the broad recovery category.
    /// </summary>
    public ErrorKind Kind { get; }

    /// <summary>
    /// Gets a safe, non-localized diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a validated expected error.
    /// </summary>
    public static Error Create(ErrorCode code, ErrorKind kind, string safeMessage)
    {
        if (!code.IsDefined)
        {
            throw new ArgumentException("An error requires a defined error code.", nameof(code));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The error kind is undefined.");
        }

        ArgumentNullException.ThrowIfNull(safeMessage);

        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            throw new ArgumentException("An error requires a non-blank safe message.", nameof(safeMessage));
        }

        return new Error(code, kind, safeMessage);
    }

    /// <inheritdoc />
    public bool Equals(Error? other) =>
        other is not null &&
        Code == other.Code &&
        Kind == other.Kind &&
        string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Error other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Code, Kind, Message);

    public static bool operator ==(Error? left, Error? right) => Equals(left, right);

    public static bool operator !=(Error? left, Error? right) => !Equals(left, right);

    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Message}";
}
