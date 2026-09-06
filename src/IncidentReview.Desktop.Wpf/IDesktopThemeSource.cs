namespace IncidentReview.Desktop.Wpf;

/// <summary>Observes the Windows application theme without owning UI preference state.</summary>
public interface IDesktopThemeSource : IDisposable
{
    /// <summary>Gets the concrete Windows application theme most recently observed.</summary>
    public ResolvedTheme CurrentTheme { get; }

    /// <summary>Occurs after Windows reports a different application theme.</summary>
    public event EventHandler<ResolvedThemeChangedEventArgs>? ThemeChanged;
}
