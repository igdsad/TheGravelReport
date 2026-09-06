namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>A deliberately test-only migration appended after the production manifest.</summary>
public sealed class SqliteTestMigration
{
    /// <summary>Creates a validated test migration.</summary>
    public SqliteTestMigration(string name, string sql)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sql);

        if (!name.StartsWith("Test_", StringComparison.Ordinal)
            || !name.EndsWith(".sql", StringComparison.Ordinal)
            || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("A test migration requires a safe Test_*.sql name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("A test migration requires non-blank SQL.", nameof(sql));
        }

        Name = name;
        Sql = sql;
    }

    /// <summary>Gets the journaled test script name.</summary>
    public string Name { get; }

    /// <summary>Gets the test-only SQL text.</summary>
    public string Sql { get; }
}
