using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class UserPreferencesTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(UserPreferences.MaximumReplayLeadInMilliseconds)]
    public void LeadInMillisecondsRoundTripAtInclusiveBoundaries(long milliseconds)
    {
        var preferences = UserPreferences.TryCreateMilliseconds(
            milliseconds,
            0.5,
            autoPause: true,
            preferredCamera: null).Value;

        Assert.AreEqual(milliseconds, preferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(milliseconds * TimeSpan.TicksPerMillisecond, preferences.ReplayLeadIn.Ticks);
        Assert.AreEqual(0.5, preferences.PlaybackSpeed);
        Assert.IsTrue(preferences.AutoPause);
        Assert.IsNull(preferences.PreferredCamera);
        Assert.AreEqual(ThemePreference.FollowDesktop, preferences.Theme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-002")]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DurationLeadInRoundTripsWithoutLoss()
    {
        var preferences = UserPreferences.TryCreate(
            TimeSpan.FromMilliseconds(5_500),
            0.25,
            autoPause: false,
            preferredCamera: "Cockpit").Value;

        Assert.AreEqual(5_500L, preferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5_500), preferences.ReplayLeadIn);
        Assert.IsFalse(preferences.AutoPause);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void LeadInRejectsNegativeSubMillisecondAndOverLimitValues()
    {
        AssertInvalid(
            UserPreferences.TryCreate(TimeSpan.FromTicks(-1), 0.5, true, null),
            "domain.user-preferences.invalid-replay-lead-in");
        AssertInvalid(
            UserPreferences.TryCreate(TimeSpan.FromTicks(1), 0.5, true, null),
            "domain.user-preferences.invalid-replay-lead-in");
        AssertInvalid(
            UserPreferences.TryCreateMilliseconds(-1, 0.5, true, null),
            "domain.user-preferences.invalid-replay-lead-in");
        AssertInvalid(
            UserPreferences.TryCreateMilliseconds(
                UserPreferences.MaximumReplayLeadInMilliseconds + 1,
                0.5,
                true,
                null),
            "domain.user-preferences.invalid-replay-lead-in");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(UserPreferences.MinimumPlaybackSpeed)]
    [DataRow(0.5)]
    [DataRow(UserPreferences.MaximumPlaybackSpeed)]
    public void PlaybackSpeedAcceptsInclusiveConservativeRange(double speed)
    {
        var preferences = UserPreferences.TryCreateMilliseconds(0, speed, true, null).Value;

        Assert.AreEqual(speed, preferences.PlaybackSpeed);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void PlaybackSpeedRejectsEveryInvalidNumericCategory()
    {
        var invalidSpeeds = new[]
        {
            double.NaN,
            double.NegativeInfinity,
            double.PositiveInfinity,
            double.BitDecrement(UserPreferences.MinimumPlaybackSpeed),
            0d,
            double.BitIncrement(UserPreferences.MaximumPlaybackSpeed),
        };

        foreach (var speed in invalidSpeeds)
        {
            AssertInvalid(
                UserPreferences.TryCreateMilliseconds(0, speed, true, null),
                "domain.user-preferences.invalid-playback-speed");
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void PreferredCameraNormalizesOptionalUserText()
    {
        var normalized = UserPreferences.TryCreateMilliseconds(
            0,
            1,
            true,
            "  Cafe\u0301  ").Value;
        var absent = UserPreferences.TryCreateMilliseconds(0, 1, true, " \t ").Value;

        Assert.AreEqual("Caf\u00e9", normalized.PreferredCamera);
        Assert.IsNull(absent.PreferredCamera);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void PreferredCameraAcceptsTheExactLimitAndAValidSurrogatePair()
    {
        var exactLimit = new string('a', UserPreferences.MaximumPreferredCameraLength);
        var withEmoji = "TV \ud83d\udcf7";

        Assert.AreEqual(
            exactLimit,
            UserPreferences.TryCreateMilliseconds(0, 1, true, exactLimit).Value.PreferredCamera);
        Assert.AreEqual(
            withEmoji,
            UserPreferences.TryCreateMilliseconds(0, 1, true, withEmoji).Value.PreferredCamera);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void PreferredCameraRejectsOverlongControlMultilineAndMalformedText()
    {
        AssertInvalidCamera(new string('a', UserPreferences.MaximumPreferredCameraLength + 1));
        AssertInvalidCamera(new string('a', (UserPreferences.MaximumPreferredCameraLength * 2) + 1));
        AssertInvalidCamera("Cock\0pit");
        AssertInvalidCamera("Cock\npit");
        AssertInvalidCamera("Cock\rpit");
        AssertInvalidCamera("Cock\r\npit");
        AssertInvalidCamera("Cock\ud800pit");
        AssertInvalidCamera("Cock\ud800Xpit");
        AssertInvalidCamera("Cock\udc00pit");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    [DataRow(ThemePreference.FollowDesktop)]
    [DataRow(ThemePreference.Light)]
    [DataRow(ThemePreference.Dark)]
    public void ThemeRoundTripsEverySupportedPreference(ThemePreference theme)
    {
        var fromMilliseconds = UserPreferences.TryCreateMilliseconds(
            5_000,
            0.5,
            autoPause: true,
            preferredCamera: "Cockpit",
            theme: theme).Value;
        var fromDuration = UserPreferences.TryCreate(
            TimeSpan.FromSeconds(5),
            0.5,
            autoPause: true,
            preferredCamera: "Cockpit",
            theme: theme).Value;

        Assert.AreEqual(theme, fromMilliseconds.Theme);
        Assert.AreEqual(theme, fromDuration.Theme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ThemeRejectsUndefinedPersistentValues()
    {
        AssertInvalid(
            UserPreferences.TryCreateMilliseconds(
                5_000,
                0.5,
                autoPause: true,
                preferredCamera: null,
                (ThemePreference)(-1)),
            "domain.user-preferences.invalid-theme");
        AssertInvalid(
            UserPreferences.TryCreateMilliseconds(
                5_000,
                0.5,
                autoPause: true,
                preferredCamera: null,
                (ThemePreference)3),
            "domain.user-preferences.invalid-theme");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ThemePersistenceCodesAreStableAndExistingFactoriesDefaultToDesktop()
    {
        var values = Enum.GetValues<ThemePreference>();
        Assert.HasCount(3, values);
        Assert.AreEqual(0, (int)values[0]);
        Assert.AreEqual(1, (int)values[1]);
        Assert.AreEqual(2, (int)values[2]);
        Assert.AreEqual(
            ThemePreference.FollowDesktop,
            UserPreferences.TryCreateMilliseconds(5_000, 1, true, null).Value.Theme);
        Assert.AreEqual(
            ThemePreference.FollowDesktop,
            UserPreferences.TryCreate(TimeSpan.FromSeconds(5), 1, true, null).Value.Theme);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void EqualNormalizedPreferencesHaveValueEquality()
    {
        var first = UserPreferences.TryCreateMilliseconds(5_000, 0.5, true, " Caf\u00e9 ").Value;
        var second = UserPreferences.TryCreateMilliseconds(5_000, 0.5, true, "Cafe\u0301").Value;
        var different = UserPreferences.TryCreateMilliseconds(
            5_000,
            0.5,
            true,
            "Caf\u00e9",
            ThemePreference.Dark).Value;

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, different);
    }

    private static void AssertInvalid(
        IncidentReview.Results.Result<UserPreferences> result,
        string errorCode) => DomainTestAssertions.IsValidationFailure(result, errorCode);

    private static void AssertInvalidCamera(string camera) => AssertInvalid(
        UserPreferences.TryCreateMilliseconds(0, 1, true, camera),
        "domain.user-preferences.invalid-preferred-camera");
}
