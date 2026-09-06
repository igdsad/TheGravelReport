using IncidentReview.Results;
using IncidentReview.Store.Contracts;

namespace IncidentReview.Host.Wpf;

internal sealed class BootstrapCoordinator(IStoreInitializer storeInitializer)
{
    public Task<Result> InitializeAsync(CancellationToken cancellationToken) =>
        storeInitializer.InitializeAsync(cancellationToken);
}
