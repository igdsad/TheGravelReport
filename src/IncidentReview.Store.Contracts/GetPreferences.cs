using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Requests the durable application preferences snapshot.</summary>
public sealed record GetPreferences : IStoreQuery<UserPreferences>
{
    private GetPreferences()
    {
    }

    /// <summary>Gets the single stateless preferences query.</summary>
    public static GetPreferences Instance { get; } = new();
}
