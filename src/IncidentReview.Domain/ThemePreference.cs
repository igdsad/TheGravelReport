namespace IncidentReview.Domain;

/// <summary>Identifies how the application chooses its visual theme.</summary>
public enum ThemePreference
{
    /// <summary>Uses the current Windows application theme.</summary>
    FollowDesktop = 0,

    /// <summary>Always uses the light application theme.</summary>
    Light = 1,

    /// <summary>Always uses the dark application theme.</summary>
    Dark = 2,
}
