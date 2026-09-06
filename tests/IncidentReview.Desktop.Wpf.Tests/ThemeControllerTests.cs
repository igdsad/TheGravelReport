using System.Windows;
using System.Windows.Media;
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

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void PalettesExposeMatchingKeysAndAccessibleCoreContrast()
    {
        using var source = new FakeDesktopThemeSource(ResolvedTheme.Light);
        using var controller = new ThemeController(source);
        var light = LoadPalette(controller, ResolvedTheme.Light);
        var dark = LoadPalette(controller, ResolvedTheme.Dark);

        CollectionAssert.AreEqual(
            light.Keys.Cast<object>().Select(static key => key.ToString()).Order().ToArray(),
            dark.Keys.Cast<object>().Select(static key => key.ToString()).Order().ToArray());
        foreach (var requiredKey in new[]
                 {
                     "WindowBackgroundBrush",
                     "SurfaceBrush",
                     "RaisedSurfaceBrush",
                     "BorderBrush",
                     "PrimaryTextBrush",
                     "SecondaryTextBrush",
                     "AccentBrush",
                     "DangerBrush",
                     "SelectionBrush",
                 })
        {
            Assert.IsTrue(light.Contains(requiredKey), $"Light palette is missing {requiredKey}.");
            Assert.IsTrue(dark.Contains(requiredKey), $"Dark palette is missing {requiredKey}.");
        }

        foreach (var retiredDuplicateKey in new[]
                 {
                     "App.Window.Background",
                     "App.Surface.Card",
                     "App.Surface.Raised",
                     "App.Border.Subtle",
                     "App.Text.Primary",
                     "App.Text.Secondary",
                     "App.Accent.Primary",
                     "App.Error.Text",
                     "App.Surface.Selected",
                 })
        {
            Assert.IsFalse(light.Contains(retiredDuplicateKey), $"Light palette still exposes {retiredDuplicateKey}.");
            Assert.IsFalse(dark.Contains(retiredDuplicateKey), $"Dark palette still exposes {retiredDuplicateKey}.");
        }

        AssertPaletteContrast(light);
        AssertPaletteContrast(dark);
    }

    private static bool IsPalette(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source?.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            source?.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static ResourceDictionary LoadPalette(
        ThemeController controller,
        ResolvedTheme theme)
    {
        var root = new ResourceDictionary();
        controller.Apply(root, theme);
        return root.MergedDictionaries.Single();
    }

    private static void AssertPaletteContrast(ResourceDictionary palette)
    {
        var surface = GetColor(palette, "SurfaceBrush");
        Assert.IsGreaterThanOrEqualTo(
            4.5,
            ContrastRatio(GetColor(palette, "PrimaryTextBrush"), surface));
        Assert.IsGreaterThanOrEqualTo(
            4.5,
            ContrastRatio(GetColor(palette, "SecondaryTextBrush"), surface));
        Assert.IsGreaterThanOrEqualTo(
            4.5,
            ContrastRatio(GetColor(palette, "App.Text.Muted"), surface));
        Assert.IsGreaterThanOrEqualTo(
            3,
            ContrastRatio(
                GetColor(palette, "App.ScrollBar.Thumb"),
                GetColor(palette, "App.ScrollBar.Track")));
    }

    private static Color GetColor(ResourceDictionary palette, string key) =>
        ((SolidColorBrush)palette[key]).Color;

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R)) +
        (0.7152 * Linearize(color.G)) +
        (0.0722 * Linearize(color.B));

    private static double Linearize(byte component)
    {
        var value = component / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
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
