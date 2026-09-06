using System.Windows;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Owns the WPF dispatcher and presents the main review window.</summary>
public sealed class IncidentReviewDesktopApplication : System.Windows.Application
{
    private readonly MainWindow _mainWindow;

    public IncidentReviewDesktopApplication(MainWindow mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _mainWindow.Closed += OnMainWindowClosed;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    /// <summary>Loads and applies stored preferences before exposing the application run boundary.</summary>
    public Result<PreparedDesktopRun> PrepareForStartup(CancellationToken cancellationToken)
    {
        var preparation = _mainWindow.ViewModel.PrepareStoredPreferencesForStartup(
            cancellationToken);
        return preparation.IsSuccess
            ? Result<PreparedDesktopRun>.Success(new PreparedDesktopRun(this))
            : Result<PreparedDesktopRun>.Failure(preparation.Error!);
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        await _mainWindow.ViewModel.DisposeAsync().ConfigureAwait(true);
        Shutdown();
    }
}

/// <summary>Represents a desktop application whose startup state is ready to display.</summary>
public sealed class PreparedDesktopRun
{
    private readonly IncidentReviewDesktopApplication _application;

    internal PreparedDesktopRun(IncidentReviewDesktopApplication application)
    {
        _application = application;
    }

    /// <summary>Enters the prepared application's message loop.</summary>
    public int Run() => _application.Run();
}
