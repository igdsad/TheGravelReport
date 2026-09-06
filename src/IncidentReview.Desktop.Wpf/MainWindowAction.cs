using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Describes data that the pure desktop-state reducer can apply.</summary>
public abstract class MainWindowAction
{
    private protected MainWindowAction()
    {
    }

    /// <summary>Atomically replaces application-owned presentation data.</summary>
    public sealed class ApplySnapshot : MainWindowAction
    {
        public ApplySnapshot(
            ReviewSnapshot snapshot,
            ResolvedTheme resolvedTheme,
            SnapshotRefreshMode refreshMode,
            DateTimeOffset occurredAt)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!Enum.IsDefined(refreshMode))
            {
                throw new ArgumentOutOfRangeException(nameof(refreshMode));
            }

            if (!Enum.IsDefined(resolvedTheme))
            {
                throw new ArgumentOutOfRangeException(nameof(resolvedTheme));
            }

            Snapshot = snapshot;
            ResolvedTheme = resolvedTheme;
            RefreshMode = refreshMode;
            OccurredAt = occurredAt;
        }

        public ReviewSnapshot Snapshot { get; }

        public ResolvedTheme ResolvedTheme { get; }

        public SnapshotRefreshMode RefreshMode { get; }

        public DateTimeOffset OccurredAt { get; }
    }

    /// <summary>Applies one complete preference value and its resolved visual palette.</summary>
    public sealed class ApplyPreferences : MainWindowAction
    {
        public ApplyPreferences(UserPreferences preferences, ResolvedTheme resolvedTheme)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            if (!Enum.IsDefined(resolvedTheme))
            {
                throw new ArgumentOutOfRangeException(nameof(resolvedTheme));
            }

            Preferences = preferences;
            ResolvedTheme = resolvedTheme;
        }

        public UserPreferences Preferences { get; }

        public ResolvedTheme ResolvedTheme { get; }
    }

    /// <summary>Applies a changed Windows palette only while following the desktop.</summary>
    public sealed class DesktopThemeChanged : MainWindowAction
    {
        public DesktopThemeChanged(ResolvedTheme resolvedTheme)
        {
            if (!Enum.IsDefined(resolvedTheme))
            {
                throw new ArgumentOutOfRangeException(nameof(resolvedTheme));
            }

            ResolvedTheme = resolvedTheme;
        }

        public ResolvedTheme ResolvedTheme { get; }
    }

    /// <summary>Changes whether a user operation is in progress.</summary>
    public sealed class SetBusy : MainWindowAction
    {
        public SetBusy(bool isBusy)
        {
            IsBusy = isBusy;
        }

        public bool IsBusy { get; }
    }

    /// <summary>Changes whether live application updates are being observed.</summary>
    public sealed class SetMonitoring : MainWindowAction
    {
        public SetMonitoring(bool isMonitoring, DateTimeOffset occurredAt)
        {
            IsMonitoring = isMonitoring;
            OccurredAt = occurredAt;
        }

        public bool IsMonitoring { get; }

        public DateTimeOffset OccurredAt { get; }
    }

    /// <summary>Changes the incident selected for keyboard activation.</summary>
    public sealed class SelectIncident : MainWindowAction
    {
        public SelectIncident(IncidentId? incidentId)
        {
            IncidentId = incidentId;
        }

        public IncidentId? IncidentId { get; }
    }

    /// <summary>Changes the unsaved preferred-camera draft.</summary>
    public sealed class SelectCamera : MainWindowAction
    {
        public SelectCamera(string? cameraName)
        {
            CameraName = cameraName;
        }

        public string? CameraName { get; }
    }

    /// <summary>Adds one event and makes that exact event the current status.</summary>
    public sealed class ShowNotice : MainWindowAction
    {
        public ShowNotice(DateTimeOffset occurredAt, string message, bool isError)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            OccurredAt = occurredAt;
            Message = message;
            IsError = isError;
        }

        public DateTimeOffset OccurredAt { get; }

        public string Message { get; }

        public bool IsError { get; }
    }
}
