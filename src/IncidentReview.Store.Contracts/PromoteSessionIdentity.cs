using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>
/// Atomically replaces a legacy durable-session identity with the identity derived from its
/// simulator-owned evidence while preserving the session's dependent records.
/// </summary>
public sealed record PromoteSessionIdentity : IStoreCommand
{
    private PromoteSessionIdentity(
        OperationId operationId,
        SessionIdentity existingIdentity,
        SessionIdentity deterministicIdentity,
        SimulatorSessionDescriptor descriptor,
        UtcInstant promotedAt)
    {
        OperationId = operationId;
        ExistingIdentity = existingIdentity;
        DeterministicIdentity = deterministicIdentity;
        Descriptor = descriptor;
        PromotedAt = promotedAt;
    }

    public OperationId OperationId { get; }

    public SessionIdentity ExistingIdentity { get; }

    public SessionIdentity DeterministicIdentity { get; }

    public SimulatorSessionDescriptor Descriptor { get; }

    public UtcInstant PromotedAt { get; }

    public static Result<PromoteSessionIdentity> TryCreate(
        SessionIdentity? existingIdentity,
        SimulatorSessionDescriptor? descriptor,
        UtcInstant? promotedAt)
    {
        if (existingIdentity is null ||
            existingIdentity.Value.Version != 7 ||
            descriptor is null ||
            descriptor.IdentityScope != SimulatorIdentityScope.Durable ||
            promotedAt is null)
        {
            return Result<PromoteSessionIdentity>.Failure(StoreErrors.InvalidCommand);
        }

        var deterministicIdentity = SessionIdentity.CreateDurable(
            descriptor.Simulator,
            descriptor.SessionKey);
        return Result<PromoteSessionIdentity>.Success(new PromoteSessionIdentity(
            OperationId.Create(),
            existingIdentity,
            deterministicIdentity,
            descriptor,
            promotedAt));
    }
}
