using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentReview.Desktop.Wpf.DependencyInjection;

/// <summary>Registers the WPF presentation boundary.</summary>
public static class DesktopServiceCollectionExtensions
{
    /// <summary>Adds the incident-review WPF application, window, and view model.</summary>
    public static IServiceCollection AddIncidentReviewDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUiDispatcher>(
            static _ => new WpfUiDispatcher(Dispatcher.CurrentDispatcher));
        services.TryAddSingleton<IDesktopThemeSource, WindowsDesktopThemeSource>();
        services.TryAddSingleton<IThemeController, ThemeController>();
        services.TryAddSingleton<IGlobalShortcut>(static _ => new WindowsGlobalShortcut());
        services.TryAddSingleton<MainWindowViewModel>();
        services.TryAddSingleton<MainWindow>();
        services.TryAddSingleton<IncidentReviewDesktopApplication>();
        return services;
    }
}
