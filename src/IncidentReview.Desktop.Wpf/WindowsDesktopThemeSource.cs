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
    private IDisposable? _subscription;
    private EventHandler<ResolvedThemeChangedEventArgs>? _themeChanged;
    private ResolvedTheme _currentTheme = ResolvedTheme.Light;
    private bool _isDisposed;

    /// <summary>Creates the production registry and SystemEvents observer.</summary>
    public WindowsDesktopThemeSource()
        : this(
            ReadAppsUseLightTheme,
            SubscribeToUserPreferenceChanges)
    {
    }

    /// <summary>
    /// Creates an observer around explicit operating-system seams. This overload keeps
    /// registry interpretation and static-event lifetime behavior independently testable.
    /// </summary>
    public WindowsDesktopThemeSource(
        Func<object?> readAppsUseLightTheme,
        Func<Action, IDisposable> subscribe)
    {
        ArgumentNullException.ThrowIfNull(readAppsUseLightTheme);
        ArgumentNullException.ThrowIfNull(subscribe);

        _readAppsUseLightTheme = readAppsUseLightTheme;
        _subscription = TrySubscribe(subscribe, OnUserPreferenceChanged);
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
        IDisposable? subscription = null;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            subscription = _subscription;
            _subscription = null;
            _themeChanged = null;
        }

        if (subscription is not null)
        {
            TryDispose(subscription);
        }
    }

    private void OnUserPreferenceChanged()
    {
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

    private static IDisposable? TrySubscribe(
        Func<Action, IDisposable> subscribe,
        Action handler)
    {
        try
        {
            return subscribe(handler);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static IDisposable SubscribeToUserPreferenceChanges(Action notify)
    {
        UserPreferenceChangedEventHandler handler = (_, _) => notify();
        SystemEvents.UserPreferenceChanged += handler;
        return new CallbackSubscription(
            () => SystemEvents.UserPreferenceChanged -= handler);
    }

    private static void TryDispose(IDisposable subscription)
    {
        try
        {
            subscription.Dispose();
        }
        catch (InvalidOperationException)
        {
        }
        catch (ExternalException)
        {
        }
    }

    private sealed class CallbackSubscription : IDisposable
    {
        private Action? _unsubscribe;

        public CallbackSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
