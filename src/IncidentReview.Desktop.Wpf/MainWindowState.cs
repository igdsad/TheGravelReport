using System.Collections.ObjectModel;
using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Contains the complete immutable state rendered by the main window.</summary>
public sealed class MainWindowState
{
    internal MainWindowState(
        bool isInitialized,
        long revision,
        ReviewServiceStatus status,
        Error? statusError,
        ReviewSession? activeSession,
        UserPreferences savedPreferences,
        IEnumerable<IncidentListItem> incidents,
        IEnumerable<CustomEventListItem> customEvents,
        IncidentId? selectedIncidentId,
        string? activeDriverName,
        string? currentCameraName,
        IEnumerable<CameraChoice> cameraChoices,
        CameraChoice selectedCameraChoice,
        bool isCameraSelectionDirty,
        string customEventSubmitterDraft,
        string customEventKeyDraft,
        string eventJoinCodeDraft,
        string eventHostAddressDraft,
        bool isCustomEventSettingsDirty,
        bool isHostingCustomEventSession,
        ResolvedTheme resolvedTheme,
        bool isBusy,
        bool isMonitoring,
        IEnumerable<EventLogItem> eventLog,
        EventLogItem? currentStatusEvent)
    {
        ArgumentNullException.ThrowIfNull(savedPreferences);
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(customEvents);
        ArgumentNullException.ThrowIfNull(cameraChoices);
        ArgumentNullException.ThrowIfNull(selectedCameraChoice);
        ArgumentNullException.ThrowIfNull(customEventSubmitterDraft);
        ArgumentNullException.ThrowIfNull(customEventKeyDraft);
        ArgumentNullException.ThrowIfNull(eventJoinCodeDraft);
        ArgumentNullException.ThrowIfNull(eventHostAddressDraft);
        ArgumentNullException.ThrowIfNull(eventLog);

        IsInitialized = isInitialized;
        Revision = revision;
        Status = status;
        StatusError = statusError;
        ActiveSession = activeSession;
        SavedPreferences = savedPreferences;
        Incidents = Copy(incidents);
        CustomEvents = Copy(customEvents);
        SelectedIncidentId = selectedIncidentId;
        ActiveDriverName = activeDriverName;
        CurrentCameraName = currentCameraName;
        CameraChoices = Copy(cameraChoices);
        SelectedCameraChoice = selectedCameraChoice;
        IsCameraSelectionDirty = isCameraSelectionDirty;
        CustomEventSubmitterDraft = customEventSubmitterDraft;
        CustomEventKeyDraft = customEventKeyDraft;
        EventJoinCodeDraft = eventJoinCodeDraft;
        EventHostAddressDraft = eventHostAddressDraft;
        IsCustomEventSettingsDirty = isCustomEventSettingsDirty;
        IsHostingCustomEventSession = isHostingCustomEventSession;
        if (!Enum.IsDefined(resolvedTheme))
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedTheme));
        }

        ResolvedTheme = resolvedTheme;
        IsBusy = isBusy;
        IsMonitoring = isMonitoring;
        EventLog = Copy(eventLog);
        CurrentStatusEvent = currentStatusEvent;
    }

    public bool IsInitialized { get; }

    public long Revision { get; }

    public ReviewServiceStatus Status { get; }

    public Error? StatusError { get; }

    public ReviewSession? ActiveSession { get; }

    /// <summary>Gets the durable preferences last observed from the application.</summary>
    public UserPreferences SavedPreferences { get; }

    public IReadOnlyList<IncidentListItem> Incidents { get; }

    public IReadOnlyList<CustomEventListItem> CustomEvents { get; }

    public IncidentId? SelectedIncidentId { get; }

    public IncidentListItem? SelectedIncident => SelectedIncidentId is null
        ? null
        : Incidents.FirstOrDefault(incident => incident.Id == SelectedIncidentId);

    public string? ActiveDriverName { get; }

    public bool HasActiveDriver => ActiveDriverName is not null;

    /// <summary>Gets the camera group currently reported by the simulator.</summary>
    public string? CurrentCameraName { get; }

    public IReadOnlyList<CameraChoice> CameraChoices { get; }

    public CameraChoice SelectedCameraChoice { get; }

    public bool IsCameraSelectionDirty { get; }

    /// <summary>Gets the editable submitter-name draft.</summary>
    public string CustomEventSubmitterDraft { get; }

    /// <summary>Gets the editable global shortcut draft.</summary>
    public string CustomEventKeyDraft { get; }

    /// <summary>Gets the editable join-code draft; an empty value means local-only storage.</summary>
    public string EventJoinCodeDraft { get; }

    /// <summary>Gets the address used to start and advertise a local event server.</summary>
    public string EventHostAddressDraft { get; }

    public bool IsCustomEventSettingsDirty { get; }

    public bool IsHostingCustomEventSession { get; }

    public string CustomEventSessionActionText =>
        IsHostingCustomEventSession ? "Stop session" : "Start joinable session";

    public bool HasActiveSession => ActiveSession is not null;

    public bool HasCustomEvents => CustomEvents.Count > 0;

    public string CustomEventEmptyMessage => ActiveSession is null
        ? "Connect to an iRacing session before creating review markers."
        : "No custom review markers have been created for this session.";

    /// <summary>Gets the persisted theme policy currently represented by this state.</summary>
    public ThemePreference ConfiguredThemePreference => SavedPreferences.Theme;

    /// <summary>Gets the concrete palette that the view must render.</summary>
    public ResolvedTheme ResolvedTheme { get; }

    public bool FollowsDesktopTheme => ConfiguredThemePreference == ThemePreference.FollowDesktop;

    public bool UsesLightThemePreference => ConfiguredThemePreference == ThemePreference.Light;

    public bool UsesDarkThemePreference => ConfiguredThemePreference == ThemePreference.Dark;

    public string ThemeToolTip => ConfiguredThemePreference switch
    {
        ThemePreference.FollowDesktop => "Theme: Follow desktop. Click for light mode.",
        ThemePreference.Light => "Theme: Light. Click for dark mode.",
        ThemePreference.Dark => "Theme: Dark. Click to follow desktop.",
        _ => throw new InvalidOperationException("The theme preference is invalid."),
    };

    public bool IsBusy { get; }

    public string BusyText => IsBusy ? "Working…" : string.Empty;

    public bool IsMonitoring { get; }

    public string MonitorActionText => IsMonitoring ? "Stop live updates" : "Start live updates";

    public string MonitoringStatus => IsMonitoring ? "Live updates on" : "Live updates off";

    public IReadOnlyList<EventLogItem> EventLog { get; }

    /// <summary>
    /// Gets the same immutable event instance that supplies the center status-bar text.
    /// </summary>
    public EventLogItem? CurrentStatusEvent { get; }

    public string ConnectionStatus => !IsInitialized
        ? "Starting"
        : FormatStatus(Status);

    public string StatusDetail
    {
        get
        {
            if (IsBusy)
            {
                return "Working…";
            }

            if (CurrentStatusEvent is not null)
            {
                return CurrentStatusEvent.Message;
            }

            return Incidents.Count == 1
                ? "1 incident"
                : string.Create(CultureInfo.CurrentCulture, $"{Incidents.Count} incidents");
        }
    }

    public bool HasError => !IsBusy && CurrentStatusEvent?.IsError == true;

    public bool ShowsEmptyState => IsInitialized && !IsBusy && Incidents.Count == 0;

    public string EmptyMessage => ActiveSession is null
        ? "No review session is available yet."
        : "No incidents are available for this session.";

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) => values switch
    {
        ReadOnlyCollection<T> readOnly => readOnly,
        _ => Array.AsReadOnly(values.ToArray()),
    };

    private static string FormatStatus(ReviewServiceStatus status) => status switch
    {
        ReviewServiceStatus.Stopped => "Stopped",
        ReviewServiceStatus.WaitingForSimulator => "Waiting for iRacing",
        ReviewServiceStatus.Connected => "Connected to iRacing",
        ReviewServiceStatus.Unavailable => "iRacing unavailable",
        _ => "Unknown",
    };
}
