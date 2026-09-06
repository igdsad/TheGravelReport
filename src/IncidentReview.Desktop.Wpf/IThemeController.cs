using System.Windows;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Resolves theme policy and applies concrete WPF palette resources.</summary>
public interface IThemeController : IDisposable
{
    /// <summary>Occurs when the followed Windows desktop theme changes.</summary>
    public event EventHandler<ResolvedThemeChangedEventArgs>? DesktopThemeChanged;

    /// <summary>Resolves a preference to one concrete palette.</summary>
    public ResolvedTheme Resolve(ThemePreference preference);

    /// <summary>Replaces only the palette merged into the supplied resource root.</summary>
    public void Apply(ResourceDictionary target, ResolvedTheme resolvedTheme);
}
