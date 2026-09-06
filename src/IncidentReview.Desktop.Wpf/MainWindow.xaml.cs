using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Hosts the incident-review presentation.</summary>
public partial class MainWindow : Window
{
    private readonly IThemeController _themeController;
    private bool _initialized;

    public MainWindow(MainWindowViewModel viewModel, IThemeController themeController)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _themeController = themeController ?? throw new ArgumentNullException(nameof(themeController));
        InitializeComponent();
        DataContext = ViewModel;
        ApplyThemeFromState();
        Loaded += OnLoaded;
        Closed += OnClosed;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
