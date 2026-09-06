using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically marks one incident as reviewed.</summary>
public sealed record MarkIncidentReviewed : IStoreCommand
{
    private MarkIncidentReviewed(
        OperationId operationId,
        IncidentId incident,
        UtcInstant reviewedAt)
    {
        OperationId = operationId;
        Incident = incident;
        ReviewedAt = reviewedAt;
    }

    public OperationId OperationId { get; }
    public IncidentId Incident { get; }
    public UtcInstant ReviewedAt { get; }

    public static MarkIncidentReviewed Create(IncidentId incident, UtcInstant reviewedAt)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(reviewedAt);
        return new MarkIncidentReviewed(OperationId.Create(), incident, reviewedAt);
    }
}
