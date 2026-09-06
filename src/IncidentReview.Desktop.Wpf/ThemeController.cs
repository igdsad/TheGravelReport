using System.Windows;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Maps theme preference to a concrete, replaceable WPF palette.</summary>
public sealed class ThemeController : IThemeController
{
    private const string ComponentPathPrefix =
        "pack://application:,,,/IncidentReview.Desktop.Wpf;component/Themes/";

    private readonly object _gate = new();
    private readonly IDesktopThemeSource _desktopThemeSource;
    private EventHandler<ResolvedThemeChangedEventArgs>? _desktopThemeChanged;
    private bool _isDisposed;

    public ThemeController(IDesktopThemeSource desktopThemeSource)
    {
        ArgumentNullException.ThrowIfNull(desktopThemeSource);
        _desktopThemeSource = desktopThemeSource;
        _desktopThemeSource.ThemeChanged += OnDesktopThemeChanged;
    }

    public event EventHandler<ResolvedThemeChangedEventArgs>? DesktopThemeChanged
    {
        add
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _desktopThemeChanged += value;
            }
        }

        remove
        {
            lock (_gate)
            {
                _desktopThemeChanged -= value;
            }
        }
    }

    public ResolvedTheme Resolve(ThemePreference preference)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }

        return preference switch
        {
            ThemePreference.FollowDesktop => _desktopThemeSource.CurrentTheme,
            ThemePreference.Light => ResolvedTheme.Light,
            ThemePreference.Dark => ResolvedTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preference),
                preference,
                "Unknown theme preference."),
        };
    }

    public void Apply(ResourceDictionary target, ResolvedTheme resolvedTheme)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }

        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(resolvedTheme))
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedTheme));
        }

        var merged = target.MergedDictionaries;
        var paletteIndexes = new List<int>();
        ResourceDictionary? reusablePalette = null;
        for (var index = 0; index < merged.Count; index++)
        {
            if (!TryIdentifyPalette(merged[index], out var existingTheme))
            {
                continue;
            }

            paletteIndexes.Add(index);
            if (existingTheme == resolvedTheme && reusablePalette is null)
            {
                reusablePalette = merged[index];
            }
        }

        if (paletteIndexes.Count == 1 && reusablePalette is not null)
        {
            return;
        }

        var insertionIndex = paletteIndexes.Count == 0
            ? merged.Count
            : paletteIndexes[0];
        for (var index = paletteIndexes.Count - 1; index >= 0; index--)
        {
            merged.RemoveAt(paletteIndexes[index]);
        }

        var palette = reusablePalette ?? CreatePalette(resolvedTheme);
        merged.Insert(Math.Min(insertionIndex, merged.Count), palette);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _desktopThemeChanged = null;
        }

        _desktopThemeSource.ThemeChanged -= OnDesktopThemeChanged;
    }

    private void OnDesktopThemeChanged(object? sender, ResolvedThemeChangedEventArgs e)
    {
        _ = sender;
        EventHandler<ResolvedThemeChangedEventArgs>? changed;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            changed = _desktopThemeChanged;
        }

        changed?.Invoke(this, e);
    }

    private static ResourceDictionary CreatePalette(ResolvedTheme theme)
    {
        // Register WPF's application package and pack URI parser before loading a
        // referenced-assembly dictionary. XAML startup normally performs both steps,
        // while DI may construct the window before the Application instance itself.
        _ = System.Windows.Application.ResourceAssembly;
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        return new ResourceDictionary
        {
            Source = new Uri(
                string.Concat(ComponentPathPrefix, theme.ToString(), ".xaml"),
                UriKind.Absolute),
        };
    }

    private static bool TryIdentifyPalette(
        ResourceDictionary dictionary,
        out ResolvedTheme theme)
    {
        var source = dictionary.Source?.OriginalString.Replace('\\', '/');
        if (source?.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true)
        {
            theme = ResolvedTheme.Light;
            return true;
        }

        if (source?.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true)
        {
            theme = ResolvedTheme.Dark;
            return true;
        }

        theme = default;
        return false;
    }
}
