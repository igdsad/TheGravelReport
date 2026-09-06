using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Contains opaque simulator-owned evidence used to resolve a session.</summary>
public sealed record SimulatorSessionKey
{
    /// <summary>Gets the maximum key length in UTF-16 code units.</summary>
    public const int MaximumLength = 256;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.simulator-session-key.invalid",
        "A simulator session key must be non-blank, bounded, and well-formed text.");

    private SimulatorSessionKey(string value)
    {
        Value = value;
    }

    /// <summary>Gets the opaque key exactly as supplied.</summary>
    public string Value { get; }

    /// <summary>Validates opaque identity evidence without interpreting or normalizing it.</summary>
    public static Result<SimulatorSessionKey> TryCreate(string? value) =>
        value is not null &&
        value.Length is > 0 and <= MaximumLength &&
        !string.IsNullOrWhiteSpace(value) &&
        DomainText.IsWellFormedWithoutDisallowedControls(value, allowMultiline: false)
            ? Result<SimulatorSessionKey>.Success(new SimulatorSessionKey(value))
            : Result<SimulatorSessionKey>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value;
}
