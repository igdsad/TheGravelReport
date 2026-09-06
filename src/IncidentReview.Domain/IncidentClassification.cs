using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents an optional user-assigned category for an incident.</summary>
/// <remarks>Absence is represented by <see langword="null"/>, never by a sentinel code.</remarks>
public sealed record IncidentClassification
{
    private const int OffTrackValue = 1;
    private const int LossOfControlValue = 2;
    private const int ContactValue = 3;
    private const int UnsafeRejoinValue = 4;
    private const int OtherValue = 5;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-classification.invalid",
        "The incident classification code is unknown.");

    private readonly string _name;

    private IncidentClassification(int value, string name)
    {
        Value = value;
        _name = name;
    }

    /// <summary>Gets the off-track category. Its stable persisted value is 1.</summary>
    public static IncidentClassification OffTrack { get; } = new(OffTrackValue, nameof(OffTrack));

    /// <summary>Gets the loss-of-control category. Its stable persisted value is 2.</summary>
    public static IncidentClassification LossOfControl { get; } = new(LossOfControlValue, nameof(LossOfControl));

    /// <summary>Gets the contact category. Its stable persisted value is 3.</summary>
    public static IncidentClassification Contact { get; } = new(ContactValue, nameof(Contact));

    /// <summary>Gets the unsafe-rejoin category. Its stable persisted value is 4.</summary>
    public static IncidentClassification UnsafeRejoin { get; } = new(UnsafeRejoinValue, nameof(UnsafeRejoin));

    /// <summary>Gets the other category. Its stable persisted value is 5.</summary>
    public static IncidentClassification Other { get; } = new(OtherValue, nameof(Other));

    /// <summary>Gets the stable persisted integer code.</summary>
    public int Value { get; }

    /// <summary>Validates a persisted integer code.</summary>
    public static Result<IncidentClassification> TryCreate(int value)
    {
        var classification = value switch
        {
            OffTrackValue => OffTrack,
            LossOfControlValue => LossOfControl,
            ContactValue => Contact,
            UnsafeRejoinValue => UnsafeRejoin,
            OtherValue => Other,
            _ => null,
        };

        return classification is null
            ? Result<IncidentClassification>.Failure(InvalidError)
            : Result<IncidentClassification>.Success(classification);
    }

    /// <inheritdoc />
    public override string ToString() => _name;
}
