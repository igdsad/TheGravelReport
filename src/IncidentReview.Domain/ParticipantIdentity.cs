using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Identifies one simulator participant within an application session.
/// </summary>
public sealed record ParticipantIdentity
{
    /// <summary>Gets the maximum identity length in UTF-16 code units.</summary>
    public const int MaximumLength = 128;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.participant-identity.invalid",
        "A participant identity must be non-blank, bounded, and well-formed text.");

    private ParticipantIdentity(string value)
    {
        Value = value;
    }

    /// <summary>Gets the opaque identity exactly as supplied.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates opaque participant identity evidence without interpreting or normalizing it.
    /// </summary>
    public static Result<ParticipantIdentity> TryCreate(string? value) =>
        value is not null &&
        value.Length is > 0 and <= MaximumLength &&
        !string.IsNullOrWhiteSpace(value) &&
        DomainText.IsWellFormedWithoutDisallowedControls(value, allowMultiline: false)
            ? Result<ParticipantIdentity>.Success(new ParticipantIdentity(value))
            : Result<ParticipantIdentity>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value;
}
