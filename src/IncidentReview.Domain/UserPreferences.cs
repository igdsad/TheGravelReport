using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Contains durable, application-wide user preferences.</summary>
public sealed record UserPreferences
{
    /// <summary>Gets the maximum supported replay lead-in: 60 seconds.</summary>
    public const long MaximumReplayLeadInMilliseconds = 60_000;

    /// <summary>Gets the slowest supported playback multiplier.</summary>
    public const double MinimumPlaybackSpeed = 0.1;

    /// <summary>Gets the fastest supported playback multiplier.</summary>
    public const double MaximumPlaybackSpeed = 1.0;

    /// <summary>Gets the maximum normalized camera-name length in UTF-16 code units.</summary>
    public const int MaximumPreferredCameraLength = 128;

    private static readonly Error InvalidLeadInError = DomainValidationError.Create(
        "domain.user-preferences.invalid-replay-lead-in",
        "Replay lead-in must be a whole number of milliseconds from zero through sixty seconds.");

    private static readonly Error InvalidPlaybackSpeedError = DomainValidationError.Create(
        "domain.user-preferences.invalid-playback-speed",
        "Playback speed must be finite and from 0.1 through 1.0.");

    private static readonly Error InvalidCameraError = DomainValidationError.Create(
        "domain.user-preferences.invalid-preferred-camera",
        "The preferred camera contains invalid text or exceeds the supported length.");

    private static readonly Error InvalidThemeError = DomainValidationError.Create(
        "domain.user-preferences.invalid-theme",
        "The selected theme is not supported.");

    private UserPreferences(
        long replayLeadInMilliseconds,
        double playbackSpeed,
        bool autoPause,
        string? preferredCamera,
        ThemePreference theme)
    {
        ReplayLeadInMilliseconds = replayLeadInMilliseconds;
        PlaybackSpeed = playbackSpeed;
        AutoPause = autoPause;
        PreferredCamera = preferredCamera;
        Theme = theme;
    }

    /// <summary>Gets the configured lead-in as lossless integer milliseconds.</summary>
    public long ReplayLeadInMilliseconds { get; }

    /// <summary>Gets the configured lead-in duration.</summary>
    public TimeSpan ReplayLeadIn => TimeSpan.FromMilliseconds(ReplayLeadInMilliseconds);

    /// <summary>Gets the configured playback multiplier.</summary>
    public double PlaybackSpeed { get; }

    /// <summary>Gets whether playback should pause after seeking.</summary>
    public bool AutoPause { get; }

    /// <summary>Gets the optional normalized camera name.</summary>
    public string? PreferredCamera { get; }

    /// <summary>Gets how the application chooses its visual theme.</summary>
    public ThemePreference Theme { get; }

    /// <summary>Validates preferences supplied as a duration.</summary>
    public static Result<UserPreferences> TryCreate(
        TimeSpan replayLeadIn,
        double playbackSpeed,
        bool autoPause,
        string? preferredCamera,
        ThemePreference theme = ThemePreference.FollowDesktop)
    {
        if (replayLeadIn.Ticks < 0 ||
            replayLeadIn.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            return Result<UserPreferences>.Failure(InvalidLeadInError);
        }

        return TryCreateMilliseconds(
            replayLeadIn.Ticks / TimeSpan.TicksPerMillisecond,
            playbackSpeed,
            autoPause,
            preferredCamera,
            theme);
    }

    /// <summary>Validates preferences supplied in persistence units.</summary>
    public static Result<UserPreferences> TryCreateMilliseconds(
        long replayLeadInMilliseconds,
        double playbackSpeed,
        bool autoPause,
        string? preferredCamera,
        ThemePreference theme = ThemePreference.FollowDesktop)
    {
        if (replayLeadInMilliseconds is < 0 or > MaximumReplayLeadInMilliseconds)
        {
            return Result<UserPreferences>.Failure(InvalidLeadInError);
        }

        if (!double.IsFinite(playbackSpeed) ||
            playbackSpeed < MinimumPlaybackSpeed ||
            playbackSpeed > MaximumPlaybackSpeed)
        {
            return Result<UserPreferences>.Failure(InvalidPlaybackSpeedError);
        }

        if (!DomainText.TryNormalizeOptional(
                preferredCamera,
                MaximumPreferredCameraLength,
                allowMultiline: false,
                out var normalizedCamera))
        {
            return Result<UserPreferences>.Failure(InvalidCameraError);
        }

        if (!Enum.IsDefined(theme))
        {
            return Result<UserPreferences>.Failure(InvalidThemeError);
        }

        return Result<UserPreferences>.Success(
            new UserPreferences(
                replayLeadInMilliseconds,
                playbackSpeed,
                autoPause,
                normalizedCamera,
                theme));
    }
}
