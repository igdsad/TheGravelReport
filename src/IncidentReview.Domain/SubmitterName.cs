using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies the person who submitted a custom review event.</summary>
public sealed record SubmitterName
{
    /// <summary>Gets the maximum normalized length in UTF-16 code units.</summary>
    public const int MaximumLength = 128;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.submitter-name.invalid",
        "A submitter name must be non-blank, well-formed text within the supported length.");

    private SubmitterName(string value)
    {
        Value = value;
    }

    /// <summary>Gets the normalized display name.</summary>
    public string Value { get; }

    /// <summary>Validates and normalizes a name read from an untrusted boundary.</summary>
    public static Result<SubmitterName> TryCreate(string? value) =>
        DomainText.TryNormalizeRequired(
            value,
            MaximumLength,
            allowMultiline: false,
            out var normalized)
            ? Result<SubmitterName>.Success(new SubmitterName(normalized))
            : Result<SubmitterName>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value;
}
