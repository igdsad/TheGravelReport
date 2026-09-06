using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies an incident independently of its storage location.</summary>
public sealed record IncidentId
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-id.invalid",
        "The incident identifier must be a canonical RFC UUID version 7 value.");

    private IncidentId(Guid value)
    {
        Value = value;
    }

    /// <summary>Gets the RFC UUID version 7 value.</summary>
    public Guid Value { get; }

    /// <summary>Generates a new time-ordered incident identifier.</summary>
    public static IncidentId Generate() => new(Guid.CreateVersion7());

    /// <summary>Validates a UUID value read from an untrusted boundary.</summary>
    public static Result<IncidentId> TryCreate(Guid value) => Uuid7.IsValid(value)
        ? Result<IncidentId>.Success(new IncidentId(value))
        : Result<IncidentId>.Failure(InvalidError);

    /// <summary>Parses canonical lowercase UUID text read from an untrusted boundary.</summary>
    public static Result<IncidentId> TryParse(string? text) =>
        Uuid7.TryParseCanonical(text, out var value)
            ? Result<IncidentId>.Success(new IncidentId(value))
            : Result<IncidentId>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
