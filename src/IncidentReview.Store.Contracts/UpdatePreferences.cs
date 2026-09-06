using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Atomically replaces the durable application preferences.</summary>
public sealed record UpdatePreferences : IStoreCommand
{
    private UpdatePreferences(
        OperationId operationId,
        UserPreferences preferences,
        UtcInstant updatedAt)
    {
        OperationId = operationId;
        Preferences = preferences;
        UpdatedAt = updatedAt;
    }

    /// <inheritdoc />
    public OperationId OperationId { get; }

    /// <summary>Gets the complete validated preferences payload.</summary>
    public UserPreferences Preferences { get; }

    /// <summary>Gets the explicit audit timestamp persisted with the update.</summary>
    public UtcInstant UpdatedAt { get; }

    /// <summary>Creates one immutable command whose generated identity is retained for retries.</summary>
    public static UpdatePreferences Create(UserPreferences preferences, UtcInstant updatedAt)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(updatedAt);
        return new UpdatePreferences(OperationId.Create(), preferences, updatedAt);
    }
}
