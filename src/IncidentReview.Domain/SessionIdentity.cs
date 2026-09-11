using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Identifies one application-owned review session independently of any simulator.
/// </summary>
public sealed record SessionIdentity
{
    private static readonly Guid DurableSessionNamespace = Uuid5.Create(
        Guid.ParseExact("6ba7b810-9dad-11d1-80b4-00c04fd430c8", "D"),
        "IncidentReview.Domain.SessionIdentity.Durable.v1");

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.session-identity.invalid",
        "The session identity must be a canonical RFC UUID version 5 or version 7 value.");

    private SessionIdentity(Guid value)
    {
        Value = value;
    }

    /// <summary>Gets the RFC UUID version 5 or version 7 value.</summary>
    public Guid Value { get; }

    /// <summary>Generates a new time-ordered session identity.</summary>
    public static SessionIdentity Generate() => new(Guid.CreateVersion7());

    /// <summary>
    /// Creates the stable identity for validated durable simulator session evidence.
    /// </summary>
    public static SessionIdentity CreateDurable(
        SimulatorCode simulator,
        SimulatorSessionKey sessionKey)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        ArgumentNullException.ThrowIfNull(sessionKey);

        var simulatorNamespace = Uuid5.Create(DurableSessionNamespace, simulator.Value);
        return new SessionIdentity(Uuid5.Create(simulatorNamespace, sessionKey.Value));
    }

    /// <summary>Validates a UUID value read from an untrusted boundary.</summary>
    public static Result<SessionIdentity> TryCreate(Guid value) => SupportedIdentityUuid.IsValid(value)
        ? Result<SessionIdentity>.Success(new SessionIdentity(value))
        : Result<SessionIdentity>.Failure(InvalidError);

    /// <summary>Parses canonical lowercase UUID text read from an untrusted boundary.</summary>
    public static Result<SessionIdentity> TryParse(string? text) =>
        SupportedIdentityUuid.TryParseCanonical(text, out var value)
            ? Result<SessionIdentity>.Success(new SessionIdentity(value))
            : Result<SessionIdentity>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
