using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>
/// Atomically accepts a received event or reports that its deterministic identity already exists.
/// </summary>
public interface ICustomEventReceiver
{
    /// <summary>
    /// Persists the first event for an identity and returns <see cref="CustomEventPublishOutcome.Duplicate"/>
    /// for every later event with that identity, regardless of its other payload values.
    /// </summary>
    public Task<Result<CustomEventPublishOutcome>> ReceiveAsync(
        CustomEventSubmission submission,
        CancellationToken cancellationToken);
}
