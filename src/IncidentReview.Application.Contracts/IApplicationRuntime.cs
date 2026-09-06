using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>Owns the telemetry-processing application lifetime.</summary>
public interface IApplicationRuntime
{
    public Task Completion { get; }

    public Task<Result> StartAsync(CancellationToken cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken);
}
