using IncidentReview.EventSync.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Application;

internal sealed class DisabledCustomEventPublisher : ICustomEventPublisher
{
    public Task<Result<CustomEventPublishOutcome>> PublishAsync(
        JoinCode joinCode,
        CustomEventSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(joinCode);
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<CustomEventPublishOutcome>.Failure(
            Application.Contracts.ApplicationErrors.EventSyncUnavailable));
    }
}

internal sealed class DisabledCustomEventSessionHost : ICustomEventSessionHost
{
    public Task Completion => Task.CompletedTask;

    public Task<Result<JoinCode>> StartAsync(
        CustomEventSessionHostRequest request,
        ICustomEventReceiver receiver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(receiver);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<JoinCode>.Failure(
            Application.Contracts.ApplicationErrors.EventSyncUnavailable));
    }

    public Task<Result> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
