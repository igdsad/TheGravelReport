using Microsoft.Win32;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class WindowsDesktopThemeSourceTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void InvalidOrMissingRegistryValuesSafelyUseLightTheme()
    {
        var events = new FakePreferenceEvents();
        object? value = "0";
        using var source = new WindowsDesktopThemeSource(
            () => value,
            events.Subscribe,
            events.Unsubscribe);

        Assert.AreEqual(ResolvedTheme.Light, source.CurrentTheme);

        value = null;
        events.Raise();
        Assert.AreEqual(ResolvedTheme.Light, source.CurrentTheme);

        value = 42;
        events.Raise();
        Assert.AreEqual(ResolvedTheme.Light, source.CurrentTheme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void RegistryReadFailureSafelyUsesLightTheme()
    {
        var events = new FakePreferenceEvents();
        using var source = new WindowsDesktopThemeSource(
            static () => throw new UnauthorizedAccessException("Test failure."),
            events.Subscribe,
            events.Unsubscribe);

        Assert.AreEqual(ResolvedTheme.Light, source.CurrentTheme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void ThemeChangesOnlyPublishWhenTheResolvedValueChanges()
    {
        var events = new FakePreferenceEvents();
        object? value = 1;
        using var source = new WindowsDesktopThemeSource(
            () => value,
            events.Subscribe,
            events.Unsubscribe);
        var observed = new List<ResolvedTheme>();
        source.ThemeChanged += (_, args) => observed.Add(args.Theme);

        events.Raise();
        value = 0;
        events.Raise();
        events.Raise();
        value = 1;
        events.Raise();

        CollectionAssert.AreEqual(
            new[] { ResolvedTheme.Dark, ResolvedTheme.Light },
            observed);
        Assert.AreEqual(ResolvedTheme.Light, source.CurrentTheme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-003")]
    public void DisposeUnsubscribesFromSystemEventsExactlyOnce()
    {
        var events = new FakePreferenceEvents();
        object? value = 1;
        var source = new WindowsDesktopThemeSource(
            () => value,
            events.Subscribe,
            events.Unsubscribe);
        var changeCount = 0;
        source.ThemeChanged += (_, _) => changeCount++;

        Assert.AreEqual(1, events.SubscriptionCount);
        source.Dispose();
        source.Dispose();
        value = 0;
        events.Raise();

        Assert.AreEqual(0, events.SubscriptionCount);
        Assert.AreEqual(1, events.UnsubscribeCount);
        Assert.AreEqual(0, changeCount);
    }

    private sealed class FakePreferenceEvents
    {
        private readonly List<UserPreferenceChangedEventHandler> _handlers = [];

        public int SubscriptionCount => _handlers.Count;

        public int UnsubscribeCount { get; private set; }

        public void Subscribe(UserPreferenceChangedEventHandler handler) => _handlers.Add(handler);

        public void Unsubscribe(UserPreferenceChangedEventHandler handler)
        {
            _ = _handlers.Remove(handler);
            UnsubscribeCount++;
        }

        public void Raise()
        {
            var args = new UserPreferenceChangedEventArgs(UserPreferenceCategory.General);
            foreach (var handler in _handlers.ToArray())
            {
                handler(this, args);
            }
        }
    }
}
