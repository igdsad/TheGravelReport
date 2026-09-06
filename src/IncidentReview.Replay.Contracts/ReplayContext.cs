using System.Text;
using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>
/// Contains simulator-neutral, transient context for presenting replay controls.
/// </summary>
public sealed record ReplayContext
{
    /// <summary>Gets the maximum supported text length in UTF-16 code units.</summary>
    public const int MaximumTextLength = 128;

    /// <summary>Gets the maximum number of selectable camera groups.</summary>
    public const int MaximumCameraGroupCount = 256;

    private static readonly Error InvalidError = Error.Create(
        ReplayErrorCodes.InvalidContext,
        ErrorKind.Validation,
        "Replay context requires bounded, valid, unambiguous driver and camera-group text.");

    private ReplayContext(
        string? driverDisplayName,
        IReadOnlyList<string> cameraGroups,
        string? currentCameraGroup)
    {
        DriverDisplayName = driverDisplayName;
        CameraGroups = cameraGroups;
        CurrentCameraGroup = currentCameraGroup;
    }

    /// <summary>Gets the connected driver's display name when the simulator provides it.</summary>
    public string? DriverDisplayName { get; }

    /// <summary>Gets selectable camera-group names in simulator-defined order.</summary>
    public IReadOnlyList<string> CameraGroups { get; }

    /// <summary>Gets the currently active camera group when it is in the selectable catalog.</summary>
    public string? CurrentCameraGroup { get; }

    /// <summary>Validates and snapshots transient replay context from an integration boundary.</summary>
    public static Result<ReplayContext> TryCreate(
        string? driverDisplayName,
        IEnumerable<string>? cameraGroups,
        string? currentCameraGroup)
    {
        if (!TryNormalizeOptional(driverDisplayName, out var normalizedDriver) ||
            cameraGroups is null)
        {
            return Result<ReplayContext>.Failure(InvalidError);
        }

        var normalizedGroups = new List<string>();
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cameraGroup in cameraGroups)
        {
            if (normalizedGroups.Count == MaximumCameraGroupCount ||
                !TryNormalizeRequired(cameraGroup, out var normalizedGroup) ||
                !seenGroups.Add(normalizedGroup))
            {
                return Result<ReplayContext>.Failure(InvalidError);
            }

            normalizedGroups.Add(normalizedGroup);
        }

        if (!TryNormalizeOptional(currentCameraGroup, out var normalizedCurrent))
        {
            return Result<ReplayContext>.Failure(InvalidError);
        }

        if (normalizedCurrent is not null)
        {
            normalizedCurrent = normalizedGroups.SingleOrDefault(group =>
                string.Equals(group, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
            if (normalizedCurrent is null)
            {
                return Result<ReplayContext>.Failure(InvalidError);
            }
        }

        return Result<ReplayContext>.Success(new ReplayContext(
            normalizedDriver,
            Array.AsReadOnly(normalizedGroups.ToArray()),
            normalizedCurrent));
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
        if (candidate.Length > MaximumTextLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
