namespace IncidentReview.Desktop.Wpf;

/// <summary>Describes one simulator-neutral camera choice rendered by the desktop UI.</summary>
public sealed class CameraChoice
{
    private CameraChoice(
        string? cameraName,
        string displayName,
        bool isAvailable,
        bool isSavedUnavailable)
    {
        CameraName = cameraName;
        DisplayName = displayName;
        IsAvailable = isAvailable;
        IsSavedUnavailable = isSavedUnavailable;
    }

    /// <summary>
    /// Gets the saved camera-group name, or <see langword="null"/> to leave the
    /// simulator's current camera unchanged.
    /// </summary>
    public string? CameraName { get; }

    /// <summary>Gets the text presented to the user.</summary>
    public string DisplayName { get; }

    /// <summary>Gets whether the camera is present in the current simulator catalog.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets whether this unavailable choice is retained from durable preferences.</summary>
    public bool IsSavedUnavailable { get; }

    internal static CameraChoice Current { get; } = new(
        cameraName: null,
        displayName: "Current iRacing camera",
        isAvailable: true,
        isSavedUnavailable: false);

    internal static CameraChoice Available(string cameraName) => new(
        cameraName,
        cameraName,
        isAvailable: true,
        isSavedUnavailable: false);

    internal static CameraChoice Unavailable(string cameraName, bool isSavedUnavailable) => new(
        cameraName,
        $"{cameraName} (not currently available)",
        isAvailable: false,
        isSavedUnavailable);
}
