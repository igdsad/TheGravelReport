using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Hosts the incident-review presentation.</summary>
public partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
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

    private void OnSessionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && ViewModel.OpenSessionCommand.CanExecute(null))
        {
            ViewModel.OpenSessionCommand.Execute(null);
        }
    }

    private void OnIncidentActivated(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.ReviewCommand.CanExecute(null))
        {
            ViewModel.ReviewCommand.Execute(null);
        }
    }
}
