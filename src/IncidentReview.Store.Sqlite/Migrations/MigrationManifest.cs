using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DbUp.Engine;

namespace IncidentReview.Store.Sqlite.Migrations;

internal static class MigrationManifest
{
    private const string ResourcePrefix = "IncidentReview.Store.Sqlite.Migrations.";

    private static readonly MigrationEntry[] Entries =
    [
        new(
            "001_InitialSchema.sql",
            ResourcePrefix + "001_InitialSchema.sql",
            "d8f86828d65f37b74e72c1b4bd00af91975aae3381d58dd1a3972cbe12d01407"),
    ];

    public static IReadOnlyList<SqlScript> LoadProductionScripts()
    {
        var assembly = typeof(MigrationManifest).Assembly;
        ValidateExactResourceSet(assembly);

        var scripts = new List<SqlScript>(Entries.Length);
        foreach (var entry in Entries)
        {
            using var stream = assembly.GetManifestResourceStream(entry.ResourceName)
                ?? throw new MigrationManifestException("A declared migration resource is missing.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(checksum, entry.Sha256, StringComparison.Ordinal))
            {
                throw new MigrationManifestException("A migration resource checksum does not match its manifest.");
            }

            var sql = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            scripts.Add(new SqlScript(entry.ScriptName, sql));
        }

        return scripts;
    }

    private static void ValidateExactResourceSet(Assembly assembly)
    {
        var embeddedSql = assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var declaredSql = Entries
            .Select(static entry => entry.ResourceName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        if (!embeddedSql.SequenceEqual(declaredSql, StringComparer.Ordinal))
        {
            throw new MigrationManifestException("Embedded migrations differ from the exact manifest.");
        }
    }

    private sealed record MigrationEntry(string ScriptName, string ResourceName, string Sha256);
}

internal sealed class MigrationManifestException : Exception
{
    public MigrationManifestException(string message)
        : base(message)
    {
    }
}
