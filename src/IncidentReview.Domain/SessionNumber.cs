using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies a zero-based simulator-neutral session within an event.</summary>
public sealed record SessionNumber
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.session-number.invalid",
        "The session number must be non-negative.");

    private SessionNumber(int value)
    {
        Value = value;
    }

    /// <summary>Gets the non-negative session number.</summary>
    public int Value { get; }

    /// <summary>Validates a session number read from an untrusted boundary.</summary>
    public static Result<SessionNumber> TryCreate(int value) => value >= 0
        ? Result<SessionNumber>.Success(new SessionNumber(value))
        : Result<SessionNumber>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
