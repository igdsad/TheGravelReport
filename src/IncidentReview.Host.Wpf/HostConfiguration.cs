using System.IO;
using IncidentReview.Store.Sqlite.Options;

namespace IncidentReview.Host.Wpf;

internal sealed class HostConfiguration
{
    public const string DatabasePathKey = "DatabasePath";
    public const string StartupTimeoutSecondsKey = "StartupTimeoutSeconds";

    public const string InvalidConfigurationMessage =
        "The application startup configuration is invalid.";

    public string? DatabasePath { get; set; }

    public int StartupTimeoutSeconds { get; set; }

    public TimeSpan StartupTimeout => TimeSpan.FromSeconds(StartupTimeoutSeconds);

    public static IReadOnlyDictionary<string, string?> CreateDefaults()
    {
        var dataRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var databasePath = Path.Combine(dataRoot, "IncidentReview", "incident-review.db");
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [DatabasePathKey] = databasePath,
            [StartupTimeoutSecondsKey] = "30",
        };
    }

    public static bool IsValid(HostConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.StartupTimeoutSeconds is >= 1 and <= 120 &&
            SqliteStoreOptions.TryCreate(configuration.DatabasePath).IsSuccess;
    }

    public SqliteStoreOptions CreateStoreOptions() =>
        SqliteStoreOptions.TryCreate(DatabasePath).Value;
}
