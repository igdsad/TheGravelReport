using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents normalized progress around a lap, from zero through one.</summary>
public sealed record LapDistance
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.lap-distance.invalid",
        "Lap distance must be a finite normalized value from zero through one.");

    private LapDistance(double value)
    {
        Value = value;
    }

    /// <summary>Gets normalized lap progress.</summary>
    public double Value { get; }

    /// <summary>Validates normalized lap progress read from an untrusted boundary.</summary>
    public static Result<LapDistance> TryCreate(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            return Result<LapDistance>.Failure(InvalidError);
        }

        // Canonicalize negative zero so persistence and binary fingerprints agree.
        return Result<LapDistance>.Success(new LapDistance(value == 0 ? 0 : value));
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
}
