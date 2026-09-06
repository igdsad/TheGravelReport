using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Observes the Windows application-theme registry setting.</summary>
public sealed class WindowsDesktopThemeSource : IDesktopThemeSource
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    private readonly object _gate = new();
    private readonly Func<object?> _readAppsUseLightTheme;
    private readonly Action<UserPreferenceChangedEventHandler> _unsubscribe;
    private readonly UserPreferenceChangedEventHandler _preferenceChangedHandler;
    private EventHandler<ResolvedThemeChangedEventArgs>? _themeChanged;
    private ResolvedTheme _currentTheme = ResolvedTheme.Light;
    private bool _isSubscribed;
    private bool _isDisposed;

    /// <summary>Creates the production registry and SystemEvents observer.</summary>
    public WindowsDesktopThemeSource()
        : this(
            ReadAppsUseLightTheme,
            static handler => SystemEvents.UserPreferenceChanged += handler,
            static handler => SystemEvents.UserPreferenceChanged -= handler)
    {
    }

    /// <summary>
    /// Creates an observer around explicit operating-system seams. This overload keeps
    /// registry interpretation and static-event lifetime behavior independently testable.
    /// </summary>
    public WindowsDesktopThemeSource(
        Func<object?> readAppsUseLightTheme,
        Action<UserPreferenceChangedEventHandler> subscribe,
        Action<UserPreferenceChangedEventHandler> unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(readAppsUseLightTheme);
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        _readAppsUseLightTheme = readAppsUseLightTheme;
        _unsubscribe = unsubscribe;
        _preferenceChangedHandler = OnUserPreferenceChanged;
        _isSubscribed = TrySubscribe(subscribe, _preferenceChangedHandler);
        lock (_gate)
        {
            // SystemEvents owns a reference as soon as subscription succeeds, so the
            // initial read participates in the same synchronization as its callback.
            _currentTheme = ReadCurrentThemeSafely();
        }
    }

    public event EventHandler<ResolvedThemeChangedEventArgs>? ThemeChanged
    {
        add
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _themeChanged += value;
            }
        }

        remove
        {
            lock (_gate)
            {
                _themeChanged -= value;
            }
        }
    }

    public ResolvedTheme CurrentTheme
    {
        get
        {
            lock (_gate)
            {
                return _currentTheme;
            }
        }
    }

    public void Dispose()
    {
        var unsubscribe = false;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            unsubscribe = _isSubscribed;
            _isSubscribed = false;
            _themeChanged = null;
        }

        if (unsubscribe)
        {
            TryUnsubscribe(_unsubscribe, _preferenceChangedHandler);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        var observed = ReadCurrentThemeSafely();
        EventHandler<ResolvedThemeChangedEventArgs>? changed;
        lock (_gate)
        {
            if (_isDisposed || observed == _currentTheme)
            {
                return;
            }

            _currentTheme = observed;
            changed = _themeChanged;
        }

        changed?.Invoke(this, new ResolvedThemeChangedEventArgs(observed));
    }

    private ResolvedTheme ReadCurrentThemeSafely()
    {
        try
        {
            return ResolveRegistryValue(_readAppsUseLightTheme());
        }
        catch (UnauthorizedAccessException)
        {
            return ResolvedTheme.Light;
        }
        catch (SecurityException)
        {
            return ResolvedTheme.Light;
        }
        catch (IOException)
        {
            return ResolvedTheme.Light;
        }
        catch (ObjectDisposedException)
        {
            return ResolvedTheme.Light;
        }
    }

    private static ResolvedTheme ResolveRegistryValue(object? value) => value is int integer && integer == 0
        ? ResolvedTheme.Dark
        : ResolvedTheme.Light;

    private static object? ReadAppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
        return key?.GetValue(
            AppsUseLightThemeValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private static bool TrySubscribe(
        Action<UserPreferenceChangedEventHandler> subscribe,
        UserPreferenceChangedEventHandler handler)
    {
        try
        {
            subscribe(handler);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static void TryUnsubscribe(
        Action<UserPreferenceChangedEventHandler> unsubscribe,
        UserPreferenceChangedEventHandler handler)
    {
        try
        {
            unsubscribe(handler);
        }
        catch (InvalidOperationException)
        {
        }
        catch (ExternalException)
        {
        }
    }
}
