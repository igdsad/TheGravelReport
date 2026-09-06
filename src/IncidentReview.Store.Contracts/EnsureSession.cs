using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically creates one durable application session.</summary>
public sealed record EnsureSession : IStoreCommand
{
    private EnsureSession(
        OperationId operationId,
        SessionIdentity proposedIdentity,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt)
    {
        OperationId = operationId;
        ProposedIdentity = proposedIdentity;
        Descriptor = descriptor;
        StartedAt = startedAt;
    }

    public OperationId OperationId { get; }
    public SessionIdentity ProposedIdentity { get; }
    public SimulatorSessionDescriptor Descriptor { get; }
    public UtcInstant StartedAt { get; }

    public static EnsureSession Create(
        SessionIdentity proposedIdentity,
        SimulatorSessionDescriptor descriptor,
        UtcInstant startedAt)
    {
        ArgumentNullException.ThrowIfNull(proposedIdentity);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(startedAt);
        return new EnsureSession(OperationId.Create(), proposedIdentity, descriptor, startedAt);
    }
}
