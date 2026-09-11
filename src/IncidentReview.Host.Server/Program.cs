using IncidentReview.EventSync.Http.DependencyInjection;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.EventSync.Sqlite.DependencyInjection;
using IncidentReview.EventSync.Sqlite.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace IncidentReview.Host.Server;

internal static class Program
{
    private const int InvalidConfigurationExitCode = 2;

    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        _ = builder.Configuration.AddInMemoryCollection(
            ServerConfiguration.CreateDefaults());
        _ = builder.Configuration.AddInMemoryCollection(
            ServerConfiguration.ReadEnvironmentOverrides());
        _ = builder.Configuration.AddCommandLine(
            args,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["--database-path"] = ServerConfiguration.DatabasePathKey,
                ["--listen-url"] = ServerConfiguration.ListenUrlKey,
            });

        if (!ServerConfiguration.TryCreate(
                builder.Configuration,
                out var serverConfiguration))
        {
            Console.Error.WriteLine(
                "GravelReview server configuration is invalid. Supply one root HTTP(S) " +
                "listen URL and an absolute SQLite database path.");
            return InvalidConfigurationExitCode;
        }

        var inboxOptions = SqliteEventInboxOptions.TryCreate(
            serverConfiguration.DatabasePath);
        if (!inboxOptions.IsSuccess)
        {
            Console.Error.WriteLine(inboxOptions.Error!.Message);
            return InvalidConfigurationExitCode;
        }

        var httpOptions = EventSyncHttpOptions.Default;
        builder.WebHost.UseUrls(serverConfiguration.ListenUrl);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            options.Limits.MaxConcurrentConnections = 128;
            options.Limits.MaxConcurrentUpgradedConnections = 0;
            options.Limits.MaxRequestBodySize = httpOptions.MaximumRequestBodyBytes;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        builder.Host.ConfigureHostOptions(static options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(25));
        _ = builder.Services.AddSqliteEventSyncReceiver(inboxOptions.Value);

        var application = builder.Build();
        _ = application.MapGet(
            "/healthz",
            static () => Microsoft.AspNetCore.Http.Results.Text(
                "healthy\n",
                "text/plain"));
        _ = application.MapHttpEventSyncReceiver(httpOptions);

        await application.RunAsync();
        return 0;
    }
}
