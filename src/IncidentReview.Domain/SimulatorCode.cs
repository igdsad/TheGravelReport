using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies a simulator using a canonical application-owned code.</summary>
public sealed record SimulatorCode
{
    /// <summary>Gets the maximum code length.</summary>
    public const int MaximumLength = 32;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.simulator-code.invalid",
        "A simulator code must be a bounded lowercase ASCII identifier.");

    private SimulatorCode(string value)
    {
        Value = value;
    }

    /// <summary>Gets the canonical code.</summary>
    public string Value { get; }

    /// <summary>Validates a code read from an untrusted boundary.</summary>
    public static Result<SimulatorCode> TryCreate(string? value) => HasValidSyntax(value)
        ? Result<SimulatorCode>.Success(new SimulatorCode(value!))
        : Result<SimulatorCode>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool HasValidSyntax(string? value)
    {
        if (value is null or { Length: 0 or > MaximumLength } ||
            !char.IsAsciiLetterLower(value[0]) ||
            value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in value)
        {
            if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-' || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        return true;
    }
}
