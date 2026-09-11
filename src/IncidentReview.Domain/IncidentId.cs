using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Identifies an incident independently of its storage location.</summary>
public sealed record IncidentId
{
    private static readonly Guid DetectedIncidentNamespace = Uuid5.Create(
        Guid.ParseExact("6ba7b810-9dad-11d1-80b4-00c04fd430c8", "D"),
        "IncidentReview.Domain.IncidentId.Detected.v1");

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-id.invalid",
        "The incident identifier must be a canonical RFC UUID version 5 or version 7 value.");

    private IncidentId(Guid value)
    {
        Value = value;
    }

    /// <summary>Gets the RFC UUID version 5 or version 7 value.</summary>
    public Guid Value { get; }

    /// <summary>Generates a new time-ordered incident identifier.</summary>
    public static IncidentId Generate() => new(Guid.CreateVersion7());

    /// <summary>
    /// Creates the stable identity for one detected incident-counter transition.
    /// </summary>
    public static IncidentId CreateDetected(
        SessionIdentity sessionIdentity,
        ParticipantIdentity participantIdentity,
        CounterEpoch counterEpoch,
        IncidentPoints points)
    {
        ArgumentNullException.ThrowIfNull(sessionIdentity);
        ArgumentNullException.ThrowIfNull(participantIdentity);
        ArgumentNullException.ThrowIfNull(counterEpoch);
        ArgumentNullException.ThrowIfNull(points);

        var sessionNamespace = Uuid5.Create(
            DetectedIncidentNamespace,
            sessionIdentity.ToString());
        var participantNamespace = Uuid5.Create(
            sessionNamespace,
            participantIdentity.Value);
        var epochNamespace = Uuid5.Create(participantNamespace, counterEpoch.ToString());
        return new IncidentId(Uuid5.Create(epochNamespace, points.Total.ToString(
            System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Validates a UUID value read from an untrusted boundary.</summary>
    public static Result<IncidentId> TryCreate(Guid value) => SupportedIdentityUuid.IsValid(value)
        ? Result<IncidentId>.Success(new IncidentId(value))
        : Result<IncidentId>.Failure(InvalidError);

    /// <summary>Parses canonical lowercase UUID text read from an untrusted boundary.</summary>
    public static Result<IncidentId> TryParse(string? text) =>
        SupportedIdentityUuid.TryParseCanonical(text, out var value)
            ? Result<IncidentId>.Success(new IncidentId(value))
            : Result<IncidentId>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
