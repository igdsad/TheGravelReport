using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>Exposes simulator-neutral incident-review use cases to presentation code.</summary>
public interface IIncidentReviewService
{
    public Task<Result<ReviewServiceStatus>> GetStatusAsync(CancellationToken cancellationToken);

    public Task<Result<ReviewSession>> GetCurrentSessionAsync(CancellationToken cancellationToken);

    public Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken);

    public Task<Result<ReviewSession>> GetSessionAsync(
        SessionIdentity session,
        CancellationToken cancellationToken);

    public Task<Result> ReviewIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken);

    public Task<Result> AnnotateIncidentAsync(
        IncidentId incidentId,
        IncidentAnnotation annotation,
        CancellationToken cancellationToken);

    public Task<Result<UserPreferences>> GetPreferencesAsync(CancellationToken cancellationToken);

    public Task<Result> UpdatePreferencesAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken);

    public IAsyncEnumerable<ReviewUpdate> ObserveUpdatesAsync(CancellationToken cancellationToken);
}
