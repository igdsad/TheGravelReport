using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Applies presentation actions without side effects.</summary>
public static class MainWindowReducer
{
    private const int EventLogCapacity = 200;

    private static readonly UserPreferences DefaultPreferences =
        UserPreferences.TryCreateMilliseconds(0, 1, autoPause: false, preferredCamera: null).Value;

    /// <summary>Gets the deterministic light-fallback state used by tests and design tooling.</summary>
    public static MainWindowState InitialState { get; } = CreateInitialState(ResolvedTheme.Light);

    /// <summary>Creates the state rendered before the first application snapshot arrives.</summary>
    public static MainWindowState CreateInitialState(ResolvedTheme resolvedTheme) => new(
        isInitialized: false,
        revision: 0,
        ReviewServiceStatus.Stopped,
        statusError: null,
        activeSession: null,
        DefaultPreferences,
        incidents: [],
        customEvents: [],
        selectedIncidentId: null,
        activeDriverName: null,
        currentCameraName: null,
        cameraChoices: [CameraChoice.Current],
        selectedCameraChoice: CameraChoice.Current,
        isCameraSelectionDirty: false,
        customEventSubmitterDraft: string.Empty,
        customEventKeyDraft: DefaultPreferences.CustomEventKey,
        eventJoinCodeDraft: string.Empty,
        eventHostAddressDraft: "http://localhost:5088/",
        isCustomEventSettingsDirty: false,
        isHostingCustomEventSession: false,
        resolvedTheme,
        isBusy: false,
        isMonitoring: false,
        eventLog: [],
        currentStatusEvent: null);

    /// <summary>Returns a new immutable root containing the supplied transition.</summary>
    public static MainWindowState Reduce(MainWindowState state, MainWindowAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        return action switch
        {
            MainWindowAction.ApplySnapshot apply => ApplySnapshot(state, apply),
            MainWindowAction.ApplyPreferences preferences => Copy(
                state,
                savedPreferences: preferences.Preferences,
                customEventSubmitterDraft: preferences.Preferences.SubmitterName ?? string.Empty,
                customEventKeyDraft: preferences.Preferences.CustomEventKey,
                eventJoinCodeDraft: preferences.Preferences.EventJoinCode ?? string.Empty,
                isCustomEventSettingsDirty: false,
                resolvedTheme: preferences.ResolvedTheme),
            MainWindowAction.DesktopThemeChanged desktopTheme =>
                ApplyDesktopThemeChanged(state, desktopTheme),
            MainWindowAction.SetBusy busy => Copy(state, isBusy: busy.IsBusy),
            MainWindowAction.SetMonitoring monitoring => ApplyMonitoring(state, monitoring),
            MainWindowAction.SelectIncident selection => ApplyIncidentSelection(state, selection),
            MainWindowAction.SelectCamera selection => ApplyCameraSelection(state, selection),
            MainWindowAction.EditCustomEventSettings edit => ApplyCustomEventSettings(state, edit),
            MainWindowAction.EditEventHostAddress edit => Copy(
                state,
                eventHostAddressDraft: edit.Address),
            MainWindowAction.SetCustomEventSessionHosting hosting => Copy(
                state,
                isHostingCustomEventSession: hosting.IsHosting),
            MainWindowAction.ShowNotice notice => AppendNotice(
                state,
                new EventLogItem(notice.OccurredAt, notice.Message, notice.IsError)),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown UI action."),
        };
    }

    private static MainWindowState ApplySnapshot(
        MainWindowState state,
        MainWindowAction.ApplySnapshot action)
    {
        var snapshot = action.Snapshot;
        if (state.IsInitialized && snapshot.Revision < state.Revision)
        {
            return state;
        }

        var incidents = snapshot.ActiveSession?.Incidents
            .OrderBy(static incident => incident.ObservedAt.UnixMilliseconds)
            .ThenBy(static incident => incident.Id.Value)
            .Select(static incident => new IncidentListItem(incident))
            .ToArray() ?? [];
        var selectedIncident = state.SelectedIncidentId is not null &&
            incidents.Any(incident => incident.Id == state.SelectedIncidentId)
                ? state.SelectedIncidentId
                : null;
        var customEvents = snapshot.ActiveSession?.CustomEvents
            .OrderBy(static customEvent => customEvent.Position.SessionNumber.Value)
            .ThenBy(static customEvent => customEvent.Position.SessionTime.Milliseconds)
            .ThenBy(static customEvent => customEvent.Id.Value)
            .Select(static customEvent => new CustomEventListItem(customEvent))
            .ToArray() ?? [];

        var selectedCameraName = snapshot.Preferences.PreferredCamera;
        var cameraSelectionDirty = false;
        if (action.RefreshMode == SnapshotRefreshMode.Automatic &&
            state.IsCameraSelectionDirty &&
            !CameraNamesEqual(
                state.SelectedCameraChoice.CameraName,
                snapshot.Preferences.PreferredCamera))
        {
            selectedCameraName = state.SelectedCameraChoice.CameraName;
            cameraSelectionDirty = true;
        }

        var cameraChoices = CreateCameraChoices(
            snapshot.CameraGroups,
            snapshot.Preferences.PreferredCamera,
            selectedCameraName);
        var selectedCamera = FindCameraChoice(cameraChoices, selectedCameraName);
        var submitterDraft = snapshot.Preferences.SubmitterName ?? string.Empty;
        var customEventKeyDraft = snapshot.Preferences.CustomEventKey;
        var joinCodeDraft = snapshot.Preferences.EventJoinCode ?? string.Empty;
        var customEventSettingsDirty = false;
        if (action.RefreshMode == SnapshotRefreshMode.Automatic &&
            state.IsCustomEventSettingsDirty &&
            !CustomEventSettingsEqual(state, snapshot.Preferences))
        {
            submitterDraft = state.CustomEventSubmitterDraft;
            customEventKeyDraft = state.CustomEventKeyDraft;
            joinCodeDraft = state.EventJoinCodeDraft;
            customEventSettingsDirty = true;
        }

        var statusChanged = state.IsInitialized && state.Status != snapshot.Status;
        var next = new MainWindowState(
            isInitialized: true,
            snapshot.Revision,
            snapshot.Status,
            snapshot.StatusError,
            snapshot.ActiveSession,
            snapshot.Preferences,
            incidents,
            customEvents,
            selectedIncident,
            snapshot.DriverDisplayName,
            snapshot.CurrentCameraGroup,
            cameraChoices,
            selectedCamera,
            cameraSelectionDirty,
            submitterDraft,
            customEventKeyDraft,
            joinCodeDraft,
            state.EventHostAddressDraft,
            customEventSettingsDirty,
            state.IsHostingCustomEventSession,
            action.ResolvedTheme,
            state.IsBusy,
            state.IsMonitoring,
            state.EventLog,
            state.CurrentStatusEvent);

        if (snapshot.StatusError is not null)
        {
            if (next.CurrentStatusEvent is { IsError: true } current &&
                string.Equals(
                    current.Message,
                    snapshot.StatusError.Message,
                    StringComparison.Ordinal))
            {
                return next;
            }

            return AppendNotice(
                next,
                new EventLogItem(
                    action.OccurredAt,
                    snapshot.StatusError.Message,
                    isError: true));
        }

        if (action.RefreshMode == SnapshotRefreshMode.Manual)
        {
            var incidentCount = incidents.Length == 1
                ? "1 incident"
                : string.Create(CultureInfo.CurrentCulture, $"{incidents.Length} incidents");
            return AppendNotice(
                next,
                new EventLogItem(
                    action.OccurredAt,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Refreshed incident log: {incidentCount} loaded."),
                    isError: false));
        }

        if (statusChanged)
        {
            return AppendNotice(
                next,
                new EventLogItem(
                    action.OccurredAt,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Simulator status changed: {next.ConnectionStatus}."),
                    isError: false));
        }

        return next;
    }

    private static MainWindowState ApplyDesktopThemeChanged(
        MainWindowState state,
        MainWindowAction.DesktopThemeChanged action) =>
        state.ConfiguredThemePreference == ThemePreference.FollowDesktop
            ? Copy(state, resolvedTheme: action.ResolvedTheme)
            : state;

    private static MainWindowState ApplyMonitoring(
        MainWindowState state,
        MainWindowAction.SetMonitoring action)
    {
        if (state.IsMonitoring == action.IsMonitoring)
        {
            return state;
        }

        var next = Copy(state, isMonitoring: action.IsMonitoring);
        if (state.StatusError is not null)
        {
            return next;
        }

        return AppendNotice(
            next,
            new EventLogItem(
                action.OccurredAt,
                action.IsMonitoring ? "Live updates started." : "Live updates stopped.",
                isError: false));
    }

    private static MainWindowState ApplyIncidentSelection(
        MainWindowState state,
        MainWindowAction.SelectIncident action)
    {
        var selected = action.IncidentId is not null &&
            state.Incidents.Any(incident => incident.Id == action.IncidentId)
                ? action.IncidentId
                : null;
        if (Equals(state.SelectedIncidentId, selected))
        {
            return state;
        }

        return Copy(state, selectedIncidentId: selected, replaceSelectedIncidentId: true);
    }

    private static MainWindowState ApplyCameraSelection(
        MainWindowState state,
        MainWindowAction.SelectCamera action)
    {
        var camera = FindCameraChoice(state.CameraChoices, action.CameraName);
        var isDirty = !CameraNamesEqual(
            camera.CameraName,
            state.SavedPreferences.PreferredCamera);
        if (CameraNamesEqual(
                state.SelectedCameraChoice.CameraName,
                camera.CameraName) &&
            state.IsCameraSelectionDirty == isDirty)
        {
            return state;
        }

        return Copy(
            state,
            selectedCameraChoice: camera,
            isCameraSelectionDirty: isDirty);
    }

    private static MainWindowState ApplyCustomEventSettings(
        MainWindowState state,
        MainWindowAction.EditCustomEventSettings action)
    {
        var isDirty = !string.Equals(
                action.SubmitterName.Trim(),
                state.SavedPreferences.SubmitterName ?? string.Empty,
                StringComparison.Ordinal) ||
            !string.Equals(
                action.Key.Trim(),
                state.SavedPreferences.CustomEventKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                action.JoinCode.Trim(),
                state.SavedPreferences.EventJoinCode ?? string.Empty,
                StringComparison.Ordinal);

        if (string.Equals(action.SubmitterName, state.CustomEventSubmitterDraft, StringComparison.Ordinal) &&
            string.Equals(action.Key, state.CustomEventKeyDraft, StringComparison.Ordinal) &&
            string.Equals(action.JoinCode, state.EventJoinCodeDraft, StringComparison.Ordinal) &&
            state.IsCustomEventSettingsDirty == isDirty)
        {
            return state;
        }

        return Copy(
            state,
            customEventSubmitterDraft: action.SubmitterName,
            customEventKeyDraft: action.Key,
            eventJoinCodeDraft: action.JoinCode,
            isCustomEventSettingsDirty: isDirty);
    }

    private static MainWindowState AppendNotice(MainWindowState state, EventLogItem notice)
    {
        var retained = state.EventLog.Count >= EventLogCapacity
            ? state.EventLog.Skip(state.EventLog.Count - EventLogCapacity + 1)
            : state.EventLog;
        var eventLog = retained.Append(notice).ToArray();
        return Copy(state, eventLog: eventLog, currentStatusEvent: notice);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<CameraChoice> CreateCameraChoices(
        IReadOnlyList<string> availableCameraNames,
        string? savedCameraName,
        string? selectedCameraName)
    {
        var choices = new List<CameraChoice>(availableCameraNames.Count + 3)
        {
            CameraChoice.Current,
        };
        choices.AddRange(availableCameraNames.Select(CameraChoice.Available));
        AddUnavailableChoice(choices, savedCameraName, isSavedUnavailable: true);
        AddUnavailableChoice(
            choices,
            selectedCameraName,
            isSavedUnavailable: CameraNamesEqual(selectedCameraName, savedCameraName));
        return Array.AsReadOnly(choices.ToArray());
    }

    private static void AddUnavailableChoice(
        List<CameraChoice> choices,
        string? cameraName,
        bool isSavedUnavailable)
    {
        if (cameraName is null || choices.Any(choice => CameraNamesEqual(choice.CameraName, cameraName)))
        {
            return;
        }

        choices.Add(CameraChoice.Unavailable(cameraName, isSavedUnavailable));
    }

    private static CameraChoice FindCameraChoice(
        IReadOnlyList<CameraChoice> choices,
        string? cameraName) => choices.FirstOrDefault(
            choice => CameraNamesEqual(choice.CameraName, cameraName)) ?? CameraChoice.Current;

    private static bool CameraNamesEqual(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool CustomEventSettingsEqual(
        MainWindowState state,
        UserPreferences preferences) =>
        string.Equals(
            state.CustomEventSubmitterDraft.Trim(),
            preferences.SubmitterName ?? string.Empty,
            StringComparison.Ordinal) &&
        string.Equals(
            state.CustomEventKeyDraft.Trim(),
            preferences.CustomEventKey,
            StringComparison.Ordinal) &&
        string.Equals(
            state.EventJoinCodeDraft.Trim(),
            preferences.EventJoinCode ?? string.Empty,
            StringComparison.Ordinal);

    private static MainWindowState Copy(
        MainWindowState state,
        bool? isBusy = null,
        bool? isMonitoring = null,
        IncidentId? selectedIncidentId = null,
        bool replaceSelectedIncidentId = false,
        CameraChoice? selectedCameraChoice = null,
        bool? isCameraSelectionDirty = null,
        string? customEventSubmitterDraft = null,
        string? customEventKeyDraft = null,
        string? eventJoinCodeDraft = null,
        string? eventHostAddressDraft = null,
        bool? isCustomEventSettingsDirty = null,
        bool? isHostingCustomEventSession = null,
        UserPreferences? savedPreferences = null,
        ResolvedTheme? resolvedTheme = null,
        IEnumerable<EventLogItem>? eventLog = null,
        EventLogItem? currentStatusEvent = null) => new(
            state.IsInitialized,
            state.Revision,
            state.Status,
            state.StatusError,
            state.ActiveSession,
            savedPreferences ?? state.SavedPreferences,
            state.Incidents,
            state.CustomEvents,
            replaceSelectedIncidentId ? selectedIncidentId : state.SelectedIncidentId,
            state.ActiveDriverName,
            state.CurrentCameraName,
            state.CameraChoices,
            selectedCameraChoice ?? state.SelectedCameraChoice,
            isCameraSelectionDirty ?? state.IsCameraSelectionDirty,
            customEventSubmitterDraft ?? state.CustomEventSubmitterDraft,
            customEventKeyDraft ?? state.CustomEventKeyDraft,
            eventJoinCodeDraft ?? state.EventJoinCodeDraft,
            eventHostAddressDraft ?? state.EventHostAddressDraft,
            isCustomEventSettingsDirty ?? state.IsCustomEventSettingsDirty,
            isHostingCustomEventSession ?? state.IsHostingCustomEventSession,
            resolvedTheme ?? state.ResolvedTheme,
            isBusy ?? state.IsBusy,
            isMonitoring ?? state.IsMonitoring,
            eventLog ?? state.EventLog,
            currentStatusEvent ?? state.CurrentStatusEvent);
}
