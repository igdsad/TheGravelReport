using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>Publishes one durable local event to a joined server.</summary>
public interface ICustomEventPublisher
{
    /// <summary>
    /// Sends one event without performing transport retries. The outbox owner decides when to retry.
    /// </summary>
    public Task<Result<CustomEventPublishOutcome>> PublishAsync(
        JoinCode joinCode,
        CustomEventSubmission submission,
        CancellationToken cancellationToken);
}
