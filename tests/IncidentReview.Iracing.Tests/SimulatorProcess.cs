using System.Diagnostics;

namespace IncidentReview.Iracing.Tests;

internal sealed class SimulatorProcess : IDisposable
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);
    private readonly Process _process;
    private int _isDisposed;

    private SimulatorProcess(Process process, string memoryMapName, string eventName)
    {
        _process = process;
        MemoryMapName = memoryMapName;
        EventName = eventName;
    }

    public string MemoryMapName { get; }

    public string EventName { get; }

    public static async Task<SimulatorProcess> StartAsync()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var memoryMapName = $"Local\\IncidentReviewIracingMap_{suffix}";
        var eventName = $"Local\\IncidentReviewIracingEvent_{suffix}";
        var startInfo = new ProcessStartInfo
        {
            FileName = FindSimulatorExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(memoryMapName);
        startInfo.ArgumentList.Add(eventName);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The protocol simulator did not start.");
        var simulator = new SimulatorProcess(process, memoryMapName, eventName);
        var ready = await simulator.ReadOutputAsync().ConfigureAwait(false);
        if (!string.Equals(ready, "READY", StringComparison.Ordinal))
        {
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            simulator.Dispose();
            throw new InvalidOperationException($"Protocol simulator startup failed: {error}");
        }

        return simulator;
    }

    public async Task SendAsync(string command)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await _process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        var response = await ReadOutputAsync().ConfigureAwait(false);
        Assert.AreEqual($"OK {command}", response);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("exit");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the status check and graceful shutdown.
        }
        finally
        {
            _process.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static string FindSimulatorExecutable()
    {
        var targetDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = targetDirectory.Parent?.Name
            ?? throw new InvalidOperationException("The test configuration could not be resolved.");
        var testsDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
        var executable = Path.Combine(
            testsDirectory,
            "IncidentReview.Iracing.ProtocolSimulator",
            "bin",
            configuration,
            "net10.0-windows",
            "IncidentReview.Iracing.ProtocolSimulator.exe");
        return File.Exists(executable)
            ? executable
            : throw new FileNotFoundException(
                "The independently built protocol simulator was not found.",
                executable);
    }

    private async Task<string?> ReadOutputAsync() =>
        await _process.StandardOutput.ReadLineAsync()
            .WaitAsync(ProcessTimeout)
            .ConfigureAwait(false);
}
