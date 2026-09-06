namespace IncidentReview.Desktop.Wpf;

/// <summary>Provides the newly observed concrete desktop theme.</summary>
public sealed class ResolvedThemeChangedEventArgs : EventArgs
{
    public ResolvedThemeChangedEventArgs(ResolvedTheme theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        Theme = theme;
    }

    public ResolvedTheme Theme { get; }
}
