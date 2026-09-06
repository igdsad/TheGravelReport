using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies a non-negative continuity epoch for a cumulative counter.</summary>
public sealed record CounterEpoch
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.counter-epoch.invalid",
        "The counter epoch must be non-negative.");

    private CounterEpoch(int value)
    {
        Value = value;
    }

    /// <summary>Gets the epoch number.</summary>
    public int Value { get; }

    /// <summary>Validates an epoch number read from an untrusted boundary.</summary>
    public static Result<CounterEpoch> TryCreate(int value) => value >= 0
        ? Result<CounterEpoch>.Success(new CounterEpoch(value))
        : Result<CounterEpoch>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
