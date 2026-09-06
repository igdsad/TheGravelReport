using System.Text;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>
/// Contains one coherent, immutable application state for presentation consumers.
/// </summary>
public sealed record ReviewSnapshot
{
    /// <summary>Largest supported driver or camera name in UTF-16 code units.</summary>
    public const int MaximumContextTextLength = 128;

    /// <summary>Largest supported camera catalog.</summary>
    public const int MaximumCameraGroupCount = 256;

    private ReviewSnapshot(
        long revision,
        ReviewServiceStatus status,
        Error? statusError,
        ReviewSession? activeSession,
        UserPreferences preferences,
        string? driverDisplayName,
        IReadOnlyList<string> cameraGroups,
        string? currentCameraGroup)
    {
        Revision = revision;
        Status = status;
        StatusError = statusError;
        ActiveSession = activeSession;
        Preferences = preferences;
        DriverDisplayName = driverDisplayName;
        CameraGroups = cameraGroups;
        CurrentCameraGroup = currentCameraGroup;
    }

    /// <summary>Gets the monotonically increasing application-state revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the simulator and application runtime status.</summary>
    public ReviewServiceStatus Status { get; }

    /// <summary>Gets the exact reason for an unavailable status.</summary>
    public Error? StatusError { get; }

    /// <summary>Gets the active session and its incidents, when one is loaded.</summary>
    public ReviewSession? ActiveSession { get; }

    /// <summary>Gets the durable replay preferences used by review commands.</summary>
    public UserPreferences Preferences { get; }

    /// <summary>Gets the connected driver's display name, when available.</summary>
    public string? DriverDisplayName { get; }

    /// <summary>Gets selectable camera groups in simulator-defined order.</summary>
    public IReadOnlyList<string> CameraGroups { get; }

    /// <summary>Gets the currently active camera group, when known.</summary>
    public string? CurrentCameraGroup { get; }

    /// <summary>Validates and snapshots all presentation-visible application state.</summary>
    public static ReviewSnapshot Create(
        long revision,
        ReviewServiceStatus status,
        Error? statusError,
        ReviewSession? activeSession,
        UserPreferences preferences,
        string? driverDisplayName,
        IEnumerable<string> cameraGroups,
        string? currentCameraGroup)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);

        if (!Enum.IsDefined(status) ||
            (status == ReviewServiceStatus.Unavailable) != (statusError is not null))
        {
            throw new ArgumentException(
                "An unavailable snapshot requires exactly one status error.",
                nameof(status));
        }

        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(cameraGroups);
        if (!TryNormalizeOptional(driverDisplayName, out var normalizedDriver))
        {
            throw new ArgumentException("The driver display name is invalid.", nameof(driverDisplayName));
        }

        var normalizedGroups = new List<string>();
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cameraGroup in cameraGroups)
        {
            if (normalizedGroups.Count == MaximumCameraGroupCount ||
                !TryNormalizeRequired(cameraGroup, out var normalizedGroup) ||
                !seenGroups.Add(normalizedGroup))
            {
                throw new ArgumentException("The camera-group catalog is invalid.", nameof(cameraGroups));
            }

            normalizedGroups.Add(normalizedGroup);
        }

        if (!TryNormalizeOptional(currentCameraGroup, out var normalizedCurrent))
        {
            throw new ArgumentException("The current camera group is invalid.", nameof(currentCameraGroup));
        }

        if (normalizedCurrent is not null)
        {
            normalizedCurrent = normalizedGroups.SingleOrDefault(group =>
                string.Equals(group, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
            if (normalizedCurrent is null)
            {
                throw new ArgumentException(
                    "The current camera group must be in the camera-group catalog.",
                    nameof(currentCameraGroup));
            }
        }

        return new ReviewSnapshot(
            revision,
            status,
            statusError,
            activeSession,
            preferences,
            normalizedDriver,
            Array.AsReadOnly(normalizedGroups.ToArray()),
            normalizedCurrent);
    }

    private static bool TryNormalizeRequired(string? value, out string normalized)
    {
        if (!TryNormalizeOptional(value, out var candidate) || candidate is null)
        {
            normalized = string.Empty;
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool TryNormalizeOptional(string? value, out string? normalized)
    {
        normalized = null;
        if (value is null)
        {
            return true;
        }

        var candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return true;
        }

        for (var index = 0; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (char.IsHighSurrogate(character))
            {
                if (++index >= candidate.Length || !char.IsLowSurrogate(candidate[index]))
                {
                    return false;
                }

                continue;
            }

            if (char.IsLowSurrogate(character) || char.IsControl(character))
            {
                return false;
            }
        }

        candidate = candidate.Normalize(NormalizationForm.FormC);
        if (candidate.Length > MaximumContextTextLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
