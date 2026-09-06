using System.Windows;
using IncidentReview.Domain;

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

    /// <summary>Applies stored preferences while the window is still hidden.</summary>
    public void PrepareForStartup(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _mainWindow.ViewModel.PrepareForStartup(preferences);
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        await _mainWindow.ViewModel.DisposeAsync().ConfigureAwait(true);
        Shutdown();
    }
}
