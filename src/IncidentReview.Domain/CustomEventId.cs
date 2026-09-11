using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Identifies a user-created review marker independently of its storage location.
/// </summary>
public sealed record CustomEventId
{
    /// <summary>Gets the stable event-type component used by identity and wire mappings.</summary>
    public const string EventType = "custom";

    private static readonly Guid CustomEventNamespace = Uuid5.Create(
        Guid.ParseExact("6ba7b810-9dad-11d1-80b4-00c04fd430c8", "D"),
        $"IncidentReview.Domain.CustomEventId.{EventType}.v1");

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.custom-event-id.invalid",
        "The custom-event identifier must be a canonical RFC UUID version 5 value.");

    private CustomEventId(Guid value)
    {
        Value = value;
    }

    /// <summary>Gets the deterministic RFC UUID version 5 value.</summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates the stable marker identity for one session-relative replay position.
    /// </summary>
    public static CustomEventId CreateDeterministic(
        SessionIdentity session,
        ReplayPosition position)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);

        var sessionNamespace = Uuid5.Create(CustomEventNamespace, session.ToString());
        var replaySessionNamespace = Uuid5.Create(
            sessionNamespace,
            position.SessionNumber.Value.ToString(CultureInfo.InvariantCulture));
        return new CustomEventId(Uuid5.Create(
            replaySessionNamespace,
            position.SessionTime.Milliseconds.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Validates a deterministic UUID value read from an untrusted boundary.</summary>
    public static Result<CustomEventId> TryCreate(Guid value) => Uuid5.IsValid(value)
        ? Result<CustomEventId>.Success(new CustomEventId(value))
        : Result<CustomEventId>.Failure(InvalidError);

    /// <summary>Parses canonical lowercase UUID text read from an untrusted boundary.</summary>
    public static Result<CustomEventId> TryParse(string? text)
    {
        if (text is null ||
            !Guid.TryParseExact(text, "D", out var value) ||
            !string.Equals(text, value.ToString("D"), StringComparison.Ordinal) ||
            !Uuid5.IsValid(value))
        {
            return Result<CustomEventId>.Failure(InvalidError);
        }

        return Result<CustomEventId>.Success(new CustomEventId(value));
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
