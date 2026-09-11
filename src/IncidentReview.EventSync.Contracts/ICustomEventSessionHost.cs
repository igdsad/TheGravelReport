using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>Hosts one joinable custom-event session through a replaceable transport.</summary>
public interface ICustomEventSessionHost : IAsyncDisposable
{
    /// <summary>
    /// Gets the running server's completion. Unexpected infrastructure failure faults this task.
    /// </summary>
    public Task Completion { get; }

    /// <summary>Starts accepting events and returns the copyable join code once ready.</summary>
    public Task<Result<JoinCode>> StartAsync(
        CustomEventSessionHostRequest request,
        ICustomEventReceiver receiver,
        CancellationToken cancellationToken);

    /// <summary>Stops accepting new events and drains the server.</summary>
    public Task<Result> StopAsync(CancellationToken cancellationToken);
}
