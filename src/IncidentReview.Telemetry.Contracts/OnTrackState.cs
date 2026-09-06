namespace IncidentReview.Telemetry.Contracts;

/// <summary>
/// Describes whether the observed participant is actively on track.
/// </summary>
public enum OnTrackState
{
    /// <summary>The source cannot currently determine the state.</summary>
    Unknown = 0,

    /// <summary>The participant is not actively on track.</summary>
    NotOnTrack = 1,

    /// <summary>The participant is actively on track.</summary>
    OnTrack = 2,
}
