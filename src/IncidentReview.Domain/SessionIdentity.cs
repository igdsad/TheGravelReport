using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Identifies one application-owned review session independently of any simulator.
/// </summary>
public sealed record SessionIdentity
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.session-identity.invalid",
        "The session identity must be a canonical RFC UUID version 7 value.");

    private SessionIdentity(Guid value)
    {
        Value = value;
    }

    /// <summary>Gets the RFC UUID version 7 value.</summary>
    public Guid Value { get; }

    /// <summary>Generates a new time-ordered session identity.</summary>
    public static SessionIdentity Generate() => new(Guid.CreateVersion7());

    /// <summary>Validates a UUID value read from an untrusted boundary.</summary>
    public static Result<SessionIdentity> TryCreate(Guid value) => Uuid7.IsValid(value)
        ? Result<SessionIdentity>.Success(new SessionIdentity(value))
        : Result<SessionIdentity>.Failure(InvalidError);

    /// <summary>Parses canonical lowercase UUID text read from an untrusted boundary.</summary>
    public static Result<SessionIdentity> TryParse(string? text) =>
        Uuid7.TryParseCanonical(text, out var value)
            ? Result<SessionIdentity>.Success(new SessionIdentity(value))
            : Result<SessionIdentity>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
