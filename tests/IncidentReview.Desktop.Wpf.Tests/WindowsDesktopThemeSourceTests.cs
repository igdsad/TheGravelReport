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
            events.Subscribe);

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
            events.Subscribe);

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
            events.Subscribe);
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
            events.Subscribe);
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
        private readonly List<Action> _handlers = [];

        public int SubscriptionCount => _handlers.Count;

        public int UnsubscribeCount { get; private set; }

        public IDisposable Subscribe(Action handler)
        {
            _handlers.Add(handler);
            return new FakeSubscription(() =>
            {
                _ = _handlers.Remove(handler);
                UnsubscribeCount++;
            });
        }

        public void Raise()
        {
            foreach (var handler in _handlers.ToArray())
            {
                handler();
            }
        }

        private sealed class FakeSubscription : IDisposable
        {
            private Action? _unsubscribe;

            public FakeSubscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
