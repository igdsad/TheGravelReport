using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents a non-negative cumulative simulator incident count.</summary>
public sealed record IncidentCounter
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-counter.invalid",
        "The incident counter must be non-negative.");

    private IncidentCounter(int value)
    {
        Value = value;
    }

    /// <summary>Gets the cumulative count.</summary>
    public int Value { get; }

    /// <summary>Validates a cumulative count read from an untrusted boundary.</summary>
    public static Result<IncidentCounter> TryCreate(int value) => value >= 0
        ? Result<IncidentCounter>.Success(new IncidentCounter(value))
        : Result<IncidentCounter>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
