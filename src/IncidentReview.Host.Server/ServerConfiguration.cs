using Microsoft.Extensions.Configuration;

namespace IncidentReview.Host.Server;

internal sealed record ServerConfiguration(string ListenUrl, string DatabasePath)
{
    public const string DatabasePathKey = "DatabasePath";
    public const string ListenUrlKey = "ListenUrl";
    public const string DatabasePathEnvironmentVariable =
        "INCIDENTREVIEW_SERVER_DATABASE_PATH";
    public const string ListenUrlEnvironmentVariable =
        "INCIDENTREVIEW_SERVER_LISTEN_URL";

    private const string DefaultDatabasePath = "/var/lib/gravelreview/custom-events.db";
    private const string DefaultListenUrl = "http://0.0.0.0:5088";

    public static IReadOnlyDictionary<string, string?> CreateDefaults() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [DatabasePathKey] = DefaultDatabasePath,
            [ListenUrlKey] = DefaultListenUrl,
        };

    public static IReadOnlyDictionary<string, string?> ReadEnvironmentOverrides()
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddEnvironmentOverride(
            overrides,
            DatabasePathKey,
            DatabasePathEnvironmentVariable);
        AddEnvironmentOverride(
            overrides,
            ListenUrlKey,
            ListenUrlEnvironmentVariable);
        return overrides;
    }

    public static bool TryCreate(
        IConfiguration configuration,
        out ServerConfiguration serverConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var databasePath = configuration[DatabasePathKey];
        var listenUrl = configuration[ListenUrlKey];
        if (string.IsNullOrWhiteSpace(databasePath) ||
            !TryNormalizeListenUrl(listenUrl, out var normalizedListenUrl))
        {
            serverConfiguration = null!;
            return false;
        }

        serverConfiguration = new ServerConfiguration(
            normalizedListenUrl,
            databasePath.Trim());
        return true;
    }

    private static void AddEnvironmentOverride(
        Dictionary<string, string?> overrides,
        string configurationKey,
        string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (value is not null)
        {
            overrides.Add(configurationKey, value);
        }
    }

    private static bool TryNormalizeListenUrl(
        string? value,
        out string normalizedListenUrl)
    {
        normalizedListenUrl = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return false;
        }

        normalizedListenUrl = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
