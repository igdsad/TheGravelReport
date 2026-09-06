using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.Sqlite.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "IncidentReview.Tests",
        Guid.NewGuid().ToString("N"));

    public TemporarySqliteDatabase()
    {
        DatabasePath = Path.Combine(_directoryPath, "incident-review.db");
        var options = SqliteStoreOptions.TryCreate(DatabasePath);
        Assert.IsTrue(options.IsSuccess);
        Options = options.Value;
    }

    public string DatabasePath { get; }

    public SqliteStoreOptions Options { get; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
