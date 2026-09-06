using System.Windows;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class ThemeControllerTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void ResolveMapsExplicitAndDesktopPreferences()
    {
        using var source = new FakeDesktopThemeSource(ResolvedTheme.Dark);
        using var controller = new ThemeController(source);

        Assert.AreEqual(ResolvedTheme.Dark, controller.Resolve(ThemePreference.FollowDesktop));
        Assert.AreEqual(ResolvedTheme.Light, controller.Resolve(ThemePreference.Light));
        Assert.AreEqual(ResolvedTheme.Dark, controller.Resolve(ThemePreference.Dark));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => controller.Resolve((ThemePreference)int.MaxValue));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void ApplyReplacesOnlyPaletteAndDoesNotDuplicateBaseResources()
    {
        using var source = new FakeDesktopThemeSource(ResolvedTheme.Light);
        using var controller = new ThemeController(source);
        var target = new ResourceDictionary();
        var baseResources = new ResourceDictionary
        {
            ["Base.Marker"] = new object(),
        };
        target.MergedDictionaries.Add(baseResources);

        controller.Apply(target, ResolvedTheme.Light);

        Assert.HasCount(2, target.MergedDictionaries);
        Assert.AreSame(baseResources, target.MergedDictionaries[0]);
        var firstPalette = target.MergedDictionaries[1];
        StringAssert.EndsWith(
            firstPalette.Source.OriginalString,
            "Themes/Light.xaml",
            StringComparison.OrdinalIgnoreCase);

        controller.Apply(target, ResolvedTheme.Light);

        Assert.HasCount(2, target.MergedDictionaries);
        Assert.AreSame(baseResources, target.MergedDictionaries[0]);
        Assert.AreSame(firstPalette, target.MergedDictionaries[1]);

        controller.Apply(target, ResolvedTheme.Dark);

        Assert.HasCount(2, target.MergedDictionaries);
        Assert.AreSame(baseResources, target.MergedDictionaries[0]);
        Assert.AreNotSame(firstPalette, target.MergedDictionaries[1]);
        StringAssert.EndsWith(
            target.MergedDictionaries[1].Source.OriginalString,
            "Themes/Dark.xaml",
            StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(1, target.MergedDictionaries.Count(IsPalette));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void DesktopThemeEventsAreForwardedAndUnsubscribedOnDispose()
    {
        using var source = new FakeDesktopThemeSource(ResolvedTheme.Light);
        var controller = new ThemeController(source);
        ResolvedThemeChangedEventArgs? observed = null;
        controller.DesktopThemeChanged += (_, args) => observed = args;

        Assert.AreEqual(1, source.SubscriberCount);
        var sourceEvent = source.SetTheme(ResolvedTheme.Dark);

        Assert.AreSame(sourceEvent, observed);
        Assert.IsNotNull(observed);
        Assert.AreEqual(ResolvedTheme.Dark, observed.Theme);

        controller.Dispose();
        controller.Dispose();
        observed = null;
        _ = source.SetTheme(ResolvedTheme.Light);

        Assert.AreEqual(0, source.SubscriberCount);
        Assert.IsNull(observed);
        Assert.IsFalse(source.IsDisposed);
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => controller.Apply(new ResourceDictionary(), ResolvedTheme.Light));
    }

    private static bool IsPalette(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source?.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            source?.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed class FakeDesktopThemeSource : IDesktopThemeSource
    {
        private EventHandler<ResolvedThemeChangedEventArgs>? _themeChanged;

        public FakeDesktopThemeSource(ResolvedTheme currentTheme)
        {
            CurrentTheme = currentTheme;
        }

        public event EventHandler<ResolvedThemeChangedEventArgs>? ThemeChanged
        {
            add
            {
                _themeChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _themeChanged -= value;
                SubscriberCount--;
            }
        }

        public ResolvedTheme CurrentTheme { get; private set; }

        public int SubscriberCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public ResolvedThemeChangedEventArgs SetTheme(ResolvedTheme theme)
        {
            CurrentTheme = theme;
            var args = new ResolvedThemeChangedEventArgs(theme);
            _themeChanged?.Invoke(this, args);
            return args;
        }

        public void Dispose()
        {
            IsDisposed = true;
            _themeChanged = null;
            SubscriberCount = 0;
        }
    }
}
