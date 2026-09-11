using System.Windows;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Owns one application-wide keyboard shortcut for a WPF window.</summary>
public interface IGlobalShortcut : IDisposable
{
    /// <summary>Occurs once when Windows activates the registered shortcut.</summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Registers a WPF key-gesture string for <paramref name="owner"/>, replacing any
    /// previously registered gesture only after the replacement succeeds.
    /// </summary>
    public Result Register(Window owner, string gestureText);

    /// <summary>Releases the current shortcut and its window-message hook.</summary>
    public void Unregister();
}
