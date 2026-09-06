using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>
/// Identifies one logical store mutation across execution, retry, and reconciliation.
/// </summary>
public sealed class OperationId : IEquatable<OperationId>
{
    private const int CanonicalTextLength = 36;
    private static readonly Error InvalidOperationIdError = Error.Create(
        StoreErrorCodes.InvalidOperationId,
        ErrorKind.Validation,
        "The operation identifier must be a canonical lowercase UUIDv7 value.");

    private readonly Guid _value;

    private OperationId(Guid value)
    {
        _value = value;
    }

    /// <summary>
    /// Generates a new UUIDv7 operation identifier.
    /// </summary>
    public static OperationId Create() => new(Guid.CreateVersion7());

    /// <summary>
    /// Rehydrates a canonical lowercase UUIDv7 operation identifier.
    /// </summary>
    public static Result<OperationId> TryParse(string? canonicalValue)
    {
        if (canonicalValue is null ||
            canonicalValue.Length != CanonicalTextLength ||
            canonicalValue[14] != '7' ||
            canonicalValue[19] is not ('8' or '9' or 'a' or 'b') ||
            !Guid.TryParseExact(canonicalValue, "D", out var parsedValue) ||
            !string.Equals(canonicalValue, parsedValue.ToString("D"), StringComparison.Ordinal))
        {
            return Result<OperationId>.Failure(InvalidOperationIdError);
        }

        return Result<OperationId>.Success(new OperationId(parsedValue));
    }

    /// <inheritdoc />
    public bool Equals(OperationId? other) =>
        other is not null && _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OperationId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _value.ToString("D");

    public static bool operator ==(OperationId? left, OperationId? right) => Equals(left, right);

    public static bool operator !=(OperationId? left, OperationId? right) => !Equals(left, right);
}
