namespace IncidentReview.Host.Wpf;

/// <summary>Enforces preparation before the desktop application enters its message loop.</summary>
internal static class DesktopStartupSequence
{
    public static int Run(Action prepareForStartup, Func<int> runApplication)
    {
        ArgumentNullException.ThrowIfNull(prepareForStartup);
        ArgumentNullException.ThrowIfNull(runApplication);

        prepareForStartup();
        return runApplication();
    }
}
