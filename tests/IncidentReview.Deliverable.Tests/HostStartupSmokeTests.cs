using System.Diagnostics;

namespace IncidentReview.Deliverable.Tests;

[TestClass]
public sealed class HostStartupSmokeTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-TST-003")]
    [TestProperty("Requirement", "IR-STR-004")]
    public async Task ReleaseHostBootstrapsItsRealDatabaseAndShutsDownCleanly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "IncidentReview.Host.Wpf",
            "bin",
            "Release",
            "net10.0-windows",
            "win-x64");
        var executable = Path.Combine(hostDirectory, "IncidentReview.Host.Wpf.exe");
        Assert.IsTrue(File.Exists(executable), $"Release host not found: {executable}");

        var testRoot = Path.Combine(Path.GetTempPath(), "IncidentReview.Deliverable.Tests");
        var runDirectory = Path.Combine(testRoot, Guid.CreateVersion7().ToString("D"));
        Directory.CreateDirectory(runDirectory);
        var databasePath = Path.Combine(runDirectory, "incident-review.db");

        try
        {
            using var process = StartHost(executable, databasePath);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                Assert.Fail("The Release host did not finish its startup verification in 30 seconds.");
            }

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(databasePath));
            Assert.IsGreaterThan(0L, new FileInfo(databasePath).Length);
            Assert.IsTrue(File.Exists(Path.Combine(
                hostDirectory,
                "THIRD-PARTY-NOTICES",
                "iracing-sdk-1.20.md")));
        }
        finally
        {
            DeleteRunDirectory(testRoot, runDirectory);
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ReleaseHostUsesGravelReviewProductMetadataAndStableAssemblyIdentity()
    {
        var executable = FindReleaseHostExecutable();
        var hostAssembly = Path.ChangeExtension(executable, ".dll");
        Assert.IsTrue(File.Exists(executable), $"Release host not found: {executable}");
        Assert.IsTrue(File.Exists(hostAssembly), $"Release host assembly not found: {hostAssembly}");

        var versionInfo = FileVersionInfo.GetVersionInfo(hostAssembly);
        Assert.AreEqual("GravelReview", versionInfo.FileDescription);
        Assert.AreEqual("GravelReview", versionInfo.ProductName);
        Assert.AreEqual(
            "IncidentReview.Host.Wpf",
            System.Reflection.AssemblyName.GetAssemblyName(hostAssembly).Name);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-DEP-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    public async Task ReleaseHostRejectsInvalidBoundOptionsDuringStartup()
    {
        var executable = FindReleaseHostExecutable();
        using var process = StartHost(executable, "relative-database.db");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await process.WaitForExitAsync(timeout.Token);

        Assert.AreEqual(1, process.ExitCode);
        Assert.IsFalse(File.Exists(Path.Combine(
            Path.GetDirectoryName(executable)!,
            "relative-database.db")));
    }

    private static Process StartHost(string executable, string databasePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--verify-startup");
        startInfo.ArgumentList.Add("--database-path");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add("--startup-timeout-seconds");
        startInfo.ArgumentList.Add("10");
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The Release host process could not be started.");
    }

    private static string FindReleaseHostExecutable()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Path.Combine(
            repositoryRoot,
            "src",
            "IncidentReview.Host.Wpf",
            "bin",
            "Release",
            "net10.0-windows",
            "win-x64",
            "IncidentReview.Host.Wpf.exe");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentReview.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }

    private static void DeleteRunDirectory(string testRoot, string runDirectory)
    {
        var fullRoot = Path.GetFullPath(testRoot);
        var fullRunDirectory = Path.GetFullPath(runDirectory);
        if (!fullRunDirectory.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The test run directory escaped its owned root.");
        }

        if (Directory.Exists(fullRunDirectory))
        {
            Directory.Delete(fullRunDirectory, recursive: true);
        }
    }
}
