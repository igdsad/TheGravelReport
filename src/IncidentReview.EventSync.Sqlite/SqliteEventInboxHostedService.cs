using Microsoft.Extensions.Hosting;

namespace IncidentReview.EventSync.Sqlite;

internal sealed class SqliteEventInboxHostedService : BackgroundService
{
    private readonly SqliteEventInbox _inbox;

    public SqliteEventInboxHostedService(SqliteEventInbox inbox)
    {
        _inbox = inbox;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _inbox.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        _inbox.StartAccepting();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _inbox.StopAccepting();
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _inbox.StopAccepting();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _inbox.RunAsync();
}
