using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents the explicit review workflow state persisted for an incident.</summary>
public sealed record IncidentReviewStatus
{
    private const int PendingValue = 1;
    private const int ReviewedValue = 2;
    private const int DismissedValue = 3;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-review-status.invalid",
        "The incident review status code is unknown.");

    private readonly string _name;

    private IncidentReviewStatus(int value, string name)
    {
        Value = value;
        _name = name;
    }

    /// <summary>Gets the initial state. Its stable persisted value is 1.</summary>
    public static IncidentReviewStatus Pending { get; } = new(PendingValue, nameof(Pending));

    /// <summary>Gets the reviewed state. Its stable persisted value is 2.</summary>
    public static IncidentReviewStatus Reviewed { get; } = new(ReviewedValue, nameof(Reviewed));

    /// <summary>Gets the dismissed state. Its stable persisted value is 3.</summary>
    public static IncidentReviewStatus Dismissed { get; } = new(DismissedValue, nameof(Dismissed));

    /// <summary>Gets the stable persisted integer code.</summary>
    public int Value { get; }

    /// <summary>Validates a persisted integer code.</summary>
    public static Result<IncidentReviewStatus> TryCreate(int value)
    {
        var status = value switch
        {
            PendingValue => Pending,
            ReviewedValue => Reviewed,
            DismissedValue => Dismissed,
            _ => null,
        };

        return status is null
            ? Result<IncidentReviewStatus>.Failure(InvalidError)
            : Result<IncidentReviewStatus>.Success(status);
    }

    /// <inheritdoc />
    public override string ToString() => _name;
}
