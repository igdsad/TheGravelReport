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

    /// <summary>Gets the state rendered before the first application snapshot arrives.</summary>
    public static MainWindowState InitialState { get; } = new(
        isInitialized: false,
        revision: 0,
        ReviewServiceStatus.Stopped,
        statusError: null,
        activeSession: null,
        DefaultPreferences,
        incidents: [],
        selectedIncidentId: null,
        activeDriverName: null,
        currentCameraName: null,
        cameraChoices: [CameraChoice.Current],
        selectedCameraChoice: CameraChoice.Current,
        isCameraSelectionDirty: false,
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
            MainWindowAction.SetBusy busy => Copy(state, isBusy: busy.IsBusy),
            MainWindowAction.SetMonitoring monitoring => ApplyMonitoring(state, monitoring),
            MainWindowAction.SelectIncident selection => ApplyIncidentSelection(state, selection),
            MainWindowAction.SelectCamera selection => ApplyCameraSelection(state, selection),
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
        var statusChanged = state.IsInitialized && state.Status != snapshot.Status;
        var next = new MainWindowState(
            isInitialized: true,
            snapshot.Revision,
            snapshot.Status,
            snapshot.StatusError,
            snapshot.ActiveSession,
            snapshot.Preferences,
            incidents,
            selectedIncident,
            snapshot.DriverDisplayName,
            snapshot.CurrentCameraGroup,
            cameraChoices,
            selectedCamera,
            cameraSelectionDirty,
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
        return Copy(
            state,
            selectedCameraChoice: camera,
            isCameraSelectionDirty: isDirty);
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

    private static MainWindowState Copy(
        MainWindowState state,
        bool? isBusy = null,
        bool? isMonitoring = null,
        IncidentId? selectedIncidentId = null,
        bool replaceSelectedIncidentId = false,
        CameraChoice? selectedCameraChoice = null,
        bool? isCameraSelectionDirty = null,
        IEnumerable<EventLogItem>? eventLog = null,
        EventLogItem? currentStatusEvent = null) => new(
            state.IsInitialized,
            state.Revision,
            state.Status,
            state.StatusError,
            state.ActiveSession,
            state.SavedPreferences,
            state.Incidents,
            replaceSelectedIncidentId ? selectedIncidentId : state.SelectedIncidentId,
            state.ActiveDriverName,
            state.CurrentCameraName,
            state.CameraChoices,
            selectedCameraChoice ?? state.SelectedCameraChoice,
            isCameraSelectionDirty ?? state.IsCameraSelectionDirty,
            isBusy ?? state.IsBusy,
            isMonitoring ?? state.IsMonitoring,
            eventLog ?? state.EventLog,
            currentStatusEvent ?? state.CurrentStatusEvent);
}
