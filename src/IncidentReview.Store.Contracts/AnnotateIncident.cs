using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically replaces one incident annotation.</summary>
public sealed record AnnotateIncident : IStoreCommand
{
    private AnnotateIncident(
        OperationId operationId,
        IncidentId incident,
        IncidentAnnotation annotation,
        UtcInstant updatedAt)
    {
        OperationId = operationId;
        Incident = incident;
        Annotation = annotation;
        UpdatedAt = updatedAt;
    }

    public OperationId OperationId { get; }
    public IncidentId Incident { get; }
    public IncidentAnnotation Annotation { get; }
    public UtcInstant UpdatedAt { get; }

    public static AnnotateIncident Create(
        IncidentId incident,
        IncidentAnnotation annotation,
        UtcInstant updatedAt)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(updatedAt);
        return new AnnotateIncident(OperationId.Create(), incident, annotation, updatedAt);
    }
}
