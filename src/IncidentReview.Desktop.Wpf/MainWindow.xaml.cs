using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Hosts the incident-review presentation.</summary>
public partial class MainWindow : Window
{
    private readonly IThemeController _themeController;
    private readonly IGlobalShortcut? _globalShortcut;
    private string? _attemptedShortcutKey;
    private bool _initialized;

    public MainWindow(
        MainWindowViewModel viewModel,
        IThemeController themeController,
        IGlobalShortcut? globalShortcut = null)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _themeController = themeController ?? throw new ArgumentNullException(nameof(themeController));
        _globalShortcut = globalShortcut;
        InitializeComponent();
        DataContext = ViewModel;
        ApplyThemeFromState();
        Loaded += OnLoaded;
        Closed += OnClosed;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_globalShortcut is not null)
        {
            _globalShortcut.Activated += OnGlobalShortcutActivated;
        }
    }

    public MainWindowViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await RegisterGlobalShortcutAsync().ConfigureAwait(true);
    }

    private void OnIncidentActivated(object sender, MouseButtonEventArgs e)
    {
        var incident = ViewModel.SelectedIncident;
        if (incident is not null && ViewModel.ReviewAtCommand.CanExecute(incident))
        {
            ViewModel.ReviewAtCommand.Execute(incident);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.State))
        {
            return;
        }

        ApplyThemeFromState();
        _ = RegisterGlobalShortcutAsync();
        if (ViewModel.State.EventLog.Count == 0)
        {
            return;
        }

        var newest = ViewModel.State.EventLog[^1];
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => EventLogList.ScrollIntoView(newest)));
    }

    private void ApplyThemeFromState() =>
        _themeController.Apply(Resources, ViewModel.State.ResolvedTheme);

    private async Task RegisterGlobalShortcutAsync()
    {
        if (_globalShortcut is null)
        {
            return;
        }

        var key = ViewModel.State.SavedPreferences.CustomEventKey;
        if (string.Equals(_attemptedShortcutKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _attemptedShortcutKey = key;
        var registered = _globalShortcut.Register(this, key);
        if (!registered.IsSuccess)
        {
            await ViewModel.ReportShortcutErrorAsync(registered.Error!).ConfigureAwait(true);
        }
    }

    private void OnGlobalShortcutActivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.CreateCustomEventCommand.CanExecute(null))
        {
            ViewModel.CreateCustomEventCommand.Execute(null);
        }
    }

    private async void OnCopyJoinCode(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (string.IsNullOrWhiteSpace(ViewModel.EventJoinCode))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ViewModel.EventJoinCode);
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            var error = Error.Create(
                ErrorCode.Define("desktop.clipboard.unavailable"),
                ErrorKind.Unavailable,
                "Windows could not copy the join code to the clipboard.");
            await ViewModel.ReportShortcutErrorAsync(error).ConfigureAwait(true);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_globalShortcut is not null)
        {
            _globalShortcut.Activated -= OnGlobalShortcutActivated;
            _globalShortcut.Unregister();
        }
    }
}
