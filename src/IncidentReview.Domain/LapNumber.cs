using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents a non-negative completed or current lap number.</summary>
public sealed record LapNumber
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.lap-number.invalid",
        "The lap number must be non-negative.");

    private LapNumber(int value)
    {
        Value = value;
    }

    /// <summary>Gets the lap number.</summary>
    public int Value { get; }

    /// <summary>Validates a lap number read from an untrusted boundary.</summary>
    public static Result<LapNumber> TryCreate(int value) => value >= 0
        ? Result<LapNumber>.Success(new LapNumber(value))
        : Result<LapNumber>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
