using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Coordinates incident-review presentation exclusively through application contracts.</summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int EventLogCapacity = 200;

    [Flags]
    private enum DirtyPreferenceFields
    {
        None = 0,
        ReplayLeadIn = 1 << 0,
        PlaybackSpeed = 1 << 1,
        AutoPause = 1 << 2,
        PreferredCamera = 1 << 3,
    }

    private enum RefreshMode
    {
        UserInitiated,
        Automatic,
    }

    private readonly IIncidentReviewService _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _isBusy;
    private bool _isMonitoring;
    private bool _isDisposed;
    private string _connectionStatus = "Starting";
    private string? _errorMessage;
    private string? _unavailableStatusErrorMessage;
    private string _emptyMessage = "No incidents are available for this session.";
    private SessionListItem? _selectedSession;
    private IncidentListItem? _selectedIncident;
    private string _replayLeadInMilliseconds = "0";
    private string _playbackSpeed = "1";
    private bool _autoPause;
    private string? _preferredCamera;
    private DirtyPreferenceFields _dirtyPreferenceFields;
    private long _preferenceEditVersion;
    private bool _isApplyingPreferences;

    public MainWindowViewModel(IIncidentReviewService service, IUiDispatcher dispatcher)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        RefreshCommand = new AsyncPresentationCommand(
            token => RefreshAsync(token),
            () => !IsBusy);
        OpenSessionCommand = new AsyncPresentationCommand(
            token => OpenSelectedSessionAsync(token),
            () => !IsBusy && SelectedSession is not null);
        ReviewCommand = new AsyncPresentationCommand(
            token => ReviewSelectedIncidentAsync(token),
            () => !IsBusy && SelectedIncident is not null);
        ReviewIncidentCommand = new AsyncPresentationCommand<IncidentListItem>(
            (incident, token) => ReviewIncidentAsync(incident, token),
            _ => !IsBusy);
        SavePreferencesCommand = new AsyncPresentationCommand(
            token => SavePreferencesAsync(token),
            () => !IsBusy);
        ToggleMonitoringCommand = new AsyncPresentationCommand(
            token => ToggleMonitoringAsync(token),
            () => !IsBusy);
        CancelCommand = new PresentationCommand(CancelActiveOperation, () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SessionListItem> Sessions { get; } = [];

    public ObservableCollection<IncidentListItem> Incidents { get; } = [];

    public ObservableCollection<EventLogItem> EventLog { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand OpenSessionCommand { get; }

    public ICommand ReviewCommand { get; }

    public ICommand ReviewIncidentCommand { get; }

    public ICommand SavePreferencesCommand { get; }

    public ICommand ToggleMonitoringCommand { get; }

    public ICommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(BusyText));
                OnPropertyChanged(nameof(StatusDetail));
            }
        }
    }

    public string BusyText => IsBusy ? "Working…" : string.Empty;

    public string StatusDetail
    {
        get
        {
            if (ErrorMessage is not null)
            {
                return ErrorMessage;
            }

            if (IsBusy)
            {
                return "Working…";
            }

            return HasIncidents
                ? FormatIncidentCount(Incidents.Count)
                : EmptyMessage;
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetField(ref _isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitorActionText));
                OnPropertyChanged(nameof(MonitoringStatus));
            }
        }
    }

    public string MonitorActionText => IsMonitoring ? "Stop live updates" : "Start live updates";

    public string MonitoringStatus => IsMonitoring ? "Live updates on" : "Live updates off";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(StatusDetail));
            }
        }
    }

    public bool HasError => ErrorMessage is not null;

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set
        {
            if (SetField(ref _emptyMessage, value))
            {
                OnPropertyChanged(nameof(StatusDetail));
            }
        }
    }

    public bool HasIncidents => Incidents.Count > 0;

    public bool ShowsEmptyState => !HasIncidents && !IsBusy;

    public SessionListItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetField(ref _selectedSession, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public IncidentListItem? SelectedIncident
    {
        get => _selectedIncident;
        set
        {
            if (SetField(ref _selectedIncident, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ReplayLeadInMilliseconds
    {
        get => _replayLeadInMilliseconds;
        set
        {
            if (SetField(ref _replayLeadInMilliseconds, value))
            {
                MarkPreferenceDirty(DirtyPreferenceFields.ReplayLeadIn);
            }
        }
    }

    public string PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetField(ref _playbackSpeed, value))
            {
                MarkPreferenceDirty(DirtyPreferenceFields.PlaybackSpeed);
            }
        }
    }

    public bool AutoPause
    {
        get => _autoPause;
        set
        {
            if (SetField(ref _autoPause, value))
            {
                MarkPreferenceDirty(DirtyPreferenceFields.AutoPause);
            }
        }
    }

    public string? PreferredCamera
    {
        get => _preferredCamera;
        set
        {
            if (SetField(ref _preferredCamera, value))
            {
                MarkPreferenceDirty(DirtyPreferenceFields.PreferredCamera);
            }
        }
    }

    /// <summary>Loads initial state and starts update monitoring.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (_isDisposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reloads a consistent presentation snapshot.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            token => RefreshCoreAsync(RefreshMode.UserInitiated, token),
            cancellationToken);

    /// <summary>Loads the session selected by the user.</summary>
    public Task OpenSelectedSessionAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedSession;
        return selected is null
            ? Task.CompletedTask
            : RunUiOperationAsync(
                token => LoadSessionAsync(selected.Id, token),
                cancellationToken);
    }

    /// <summary>Requests replay review for the selected incident.</summary>
    public Task ReviewSelectedIncidentAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedIncident;
        if (selected is null)
        {
            return Task.CompletedTask;
        }

        return ReviewIncidentAsync(selected, cancellationToken);
    }

    private Task ReviewIncidentAsync(
        IncidentListItem incident,
        CancellationToken cancellationToken)
    {
        return RunUiOperationAsync(
            async token =>
            {
                var result = await _service.ReviewIncidentAsync(incident.Id, token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                await RunOnUiAsync(
                    () =>
                    {
                        AddEventLogEntry(
                            string.Create(
                                CultureInfo.CurrentCulture,
                                $"Replay requested for incident {incident.Id} at {incident.ReplayTime}."));
                        ErrorMessage = _unavailableStatusErrorMessage;
                    }).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>Validates and saves the displayed preferences.</summary>
    public Task SavePreferencesAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                if (!long.TryParse(
                        ReplayLeadInMilliseconds,
                        NumberStyles.None,
                        CultureInfo.CurrentCulture,
                        out var leadIn) ||
                    !double.TryParse(
                        PlaybackSpeed,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out var speed))
                {
                    await SetErrorAsync(
                        "Enter lead-in milliseconds and playback speed as numbers.").ConfigureAwait(false);
                    return;
                }

                var preferences = UserPreferences.TryCreateMilliseconds(
                    leadIn,
                    speed,
                    AutoPause,
                    PreferredCamera);
                if (!preferences.IsSuccess)
                {
                    await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
                    return;
                }

                var preferenceEditVersion = _preferenceEditVersion;
                var result = await _service.UpdatePreferencesAsync(preferences.Value, token).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    await RunOnUiAsync(
                        () =>
                        {
                            if (_preferenceEditVersion == preferenceEditVersion)
                            {
                                _dirtyPreferenceFields = DirtyPreferenceFields.None;
                            }

                            AddEventLogEntry("Replay preferences saved.");
                            ErrorMessage = _unavailableStatusErrorMessage;
                        }).ConfigureAwait(false);
                }
                else
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                }
            },
            cancellationToken);

    /// <summary>Starts observing application-owned update notifications.</summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (_monitorTask is not null)
        {
            if (!_monitorTask.IsCompleted)
            {
                return;
            }

            await StopMonitoringAsync().ConfigureAwait(false);
        }

        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        await RunOnUiAsync(
            () =>
            {
                IsMonitoring = true;
                AddEventLogEntry("Live updates started.");
            }).ConfigureAwait(false);
        _monitorTask = ObserveUpdatesAsync(_monitorCancellation.Token);
    }

    /// <summary>Stops and observes the presentation update worker.</summary>
    public async Task StopMonitoringAsync()
    {
        var cancellation = _monitorCancellation;
        var task = _monitorTask;
        if (task is null)
        {
            return;
        }

        cancellation!.Cancel();
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
            _monitorCancellation = null;
            _monitorTask = null;
            await RunOnUiAsync(
                () =>
                {
                    IsMonitoring = false;
                    AddEventLogEntry("Live updates stopped.");
                }).ConfigureAwait(false);
        }
    }

    /// <summary>Cancels the current user-initiated presentation operation.</summary>
    public void CancelActiveOperation() => _activeOperationCancellation?.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCancellation.Cancel();
        _activeOperationCancellation?.Cancel();
        await StopMonitoringAsync().ConfigureAwait(false);

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _operationGate.Release();
        _lifetimeCancellation.Dispose();
        _operationGate.Dispose();
    }

    private async Task ToggleMonitoringAsync(CancellationToken cancellationToken)
    {
        if (IsMonitoring)
        {
            await StopMonitoringAsync().ConfigureAwait(false);
        }
        else
        {
            await StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ObserveUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _service.ObserveUpdatesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (update is ReviewUpdate.StatusChanged statusChanged)
                {
                    await RunOnUiAsync(
                        () =>
                        {
                            ConnectionStatus = FormatStatus(statusChanged.Status);
                            _unavailableStatusErrorMessage = statusChanged.Error is null
                                ? null
                                : FormatError(statusChanged.Error);
                            ErrorMessage = _unavailableStatusErrorMessage;
                            AddEventLogEntry(
                                _unavailableStatusErrorMessage ??
                                    string.Create(
                                        CultureInfo.CurrentCulture,
                                        $"Simulator status changed: {ConnectionStatus}."),
                                statusChanged.Error is not null);
                        }).ConfigureAwait(false);

                    if (statusChanged.Error is not null)
                    {
                        continue;
                    }
                }
                else if (update is ReviewUpdate.IncidentChanged incidentChanged)
                {
                    await RunOnUiAsync(
                        () => AddEventLogEntry(
                            string.Create(
                                CultureInfo.CurrentCulture,
                                $"Incident recorded: {incidentChanged.Incident}."))).ConfigureAwait(false);
                }
                else if (update is ReviewUpdate.SessionChanged sessionChanged)
                {
                    await RunOnUiAsync(
                        () => AddEventLogEntry(
                            string.Create(
                                CultureInfo.CurrentCulture,
                                $"Session changed: {sessionChanged.Session}."))).ConfigureAwait(false);
                }

                await RunUiOperationAsync(
                    token => RefreshCoreAsync(RefreshMode.Automatic, token),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await SetErrorAsync(
                "Live updates stopped unexpectedly. Use Refresh or restart monitoring.").ConfigureAwait(false);
            await RunOnUiAsync(() => IsMonitoring = false).ConfigureAwait(false);
        }
    }

    private async Task RunUiOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            _activeOperationCancellation = linkedCancellation;
            await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
            try
            {
                await operation(linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
        }
        finally
        {
            _activeOperationCancellation = null;
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
            _operationGate.Release();
        }
    }

    private async Task RefreshCoreAsync(
        RefreshMode refreshMode,
        CancellationToken cancellationToken)
    {
        var status = await _service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            await ShowErrorAsync(status.Error!).ConfigureAwait(false);
            return;
        }

        await RunOnUiAsync(
            () =>
            {
                ConnectionStatus = FormatStatus(status.Value);
                if (status.Value != ReviewServiceStatus.Unavailable)
                {
                    _unavailableStatusErrorMessage = null;
                }
            }).ConfigureAwait(false);

        var sessions = await _service.ListSessionsAsync(SessionQuery.All, cancellationToken).ConfigureAwait(false);
        if (!sessions.IsSuccess)
        {
            await ShowErrorAsync(sessions.Error!).ConfigureAwait(false);
            return;
        }

        var current = await _service.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess && current.Error!.Code != ApplicationErrorCodes.NoCurrentSession)
        {
            await ShowErrorAsync(current.Error).ConfigureAwait(false);
            return;
        }

        var preferences = await _service.GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        if (!preferences.IsSuccess)
        {
            await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
            return;
        }

        var summaryModels = sessions.Value.ToList();
        if (current.IsSuccess && summaryModels.All(summary => summary.Id != current.Value.Id))
        {
            summaryModels.Insert(
                0,
                SessionSummary.Create(
                    current.Value.Id,
                    current.Value.Descriptor,
                    current.Value.StartedAt,
                    current.Value.Incidents.Count,
                    current.Value.Incidents.Count(
                        static incident => incident.ReviewStatus == IncidentReviewStatus.Pending)));
        }

        var summaries = summaryModels
            .Select(summary => new SessionListItem(
                summary,
                current.IsSuccess && summary.Id == current.Value.Id))
            .ToArray();
        var previousId = SelectedSession?.Id;
        var selected = summaries.FirstOrDefault(item => item.Id == previousId) ??
            (current.IsSuccess
                ? summaries.FirstOrDefault(item => item.Id == current.Value.Id)
                : null) ??
            summaries.FirstOrDefault();

        ReviewSession? reviewSession = current.IsSuccess && selected?.Id == current.Value.Id
            ? current.Value
            : null;
        if (selected is not null && reviewSession is null)
        {
            var loaded = await _service.GetSessionAsync(selected.Id, cancellationToken).ConfigureAwait(false);
            if (!loaded.IsSuccess)
            {
                await ShowErrorAsync(loaded.Error!).ConfigureAwait(false);
                return;
            }

            reviewSession = loaded.Value;
        }

        await RunOnUiAsync(
            () =>
            {
                Replace(Sessions, summaries);
                SelectedSession = selected;
                ApplySession(reviewSession);
                ApplyPreferences(
                    preferences.Value,
                    preserveDirtyFields: refreshMode == RefreshMode.Automatic);
                ErrorMessage = status.Value == ReviewServiceStatus.Unavailable
                    ? _unavailableStatusErrorMessage
                    : null;
                if (refreshMode == RefreshMode.UserInitiated)
                {
                    AddEventLogEntry(
                        string.Create(
                            CultureInfo.CurrentCulture,
                            $"Refreshed incident log: {FormatIncidentCount(Incidents.Count)} loaded."));
                }
            }).ConfigureAwait(false);
    }

    private async Task LoadSessionAsync(SessionIdentity session, CancellationToken cancellationToken)
    {
        var result = await _service.GetSessionAsync(session, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            await ShowErrorAsync(result.Error!).ConfigureAwait(false);
            return;
        }

        await RunOnUiAsync(
            () =>
            {
                ErrorMessage = _unavailableStatusErrorMessage;
                ApplySession(result.Value);
            }).ConfigureAwait(false);
    }

    private void ApplySession(ReviewSession? session)
    {
        var previousIncidentId = SelectedIncident?.Id;
        var incidents = session?.Incidents
            .OrderBy(static incident => incident.ObservedAt.UnixMilliseconds)
            .ThenBy(static incident => incident.Id.Value)
            .Select(static incident => new IncidentListItem(incident))
            .ToArray() ?? [];
        Replace(Incidents, incidents);
        SelectedIncident = incidents.FirstOrDefault(incident => incident.Id == previousIncidentId);
        EmptyMessage = session is null
            ? "No review session is available yet."
            : "No incidents are available for this session.";
        OnPropertyChanged(nameof(HasIncidents));
        OnPropertyChanged(nameof(ShowsEmptyState));
        OnPropertyChanged(nameof(StatusDetail));
    }

    private void ApplyPreferences(UserPreferences preferences, bool preserveDirtyFields)
    {
        _isApplyingPreferences = true;
        try
        {
            if (!preserveDirtyFields ||
                !_dirtyPreferenceFields.HasFlag(DirtyPreferenceFields.ReplayLeadIn))
            {
                ReplayLeadInMilliseconds = preferences.ReplayLeadInMilliseconds.ToString(
                    CultureInfo.CurrentCulture);
            }

            if (!preserveDirtyFields ||
                !_dirtyPreferenceFields.HasFlag(DirtyPreferenceFields.PlaybackSpeed))
            {
                PlaybackSpeed = preferences.PlaybackSpeed.ToString("G", CultureInfo.CurrentCulture);
            }

            if (!preserveDirtyFields ||
                !_dirtyPreferenceFields.HasFlag(DirtyPreferenceFields.AutoPause))
            {
                AutoPause = preferences.AutoPause;
            }

            if (!preserveDirtyFields ||
                !_dirtyPreferenceFields.HasFlag(DirtyPreferenceFields.PreferredCamera))
            {
                PreferredCamera = preferences.PreferredCamera;
            }

            if (!preserveDirtyFields)
            {
                _dirtyPreferenceFields = DirtyPreferenceFields.None;
            }
        }
        finally
        {
            _isApplyingPreferences = false;
        }
    }

    private void MarkPreferenceDirty(DirtyPreferenceFields field)
    {
        if (_isApplyingPreferences)
        {
            return;
        }

        _dirtyPreferenceFields |= field;
        _preferenceEditVersion++;
    }

    private Task ShowErrorAsync(Error error) => SetErrorAsync(FormatError(error));

    private Task SetErrorAsync(string? message) =>
        RunOnUiAsync(
            () =>
            {
                ErrorMessage = message;
                if (message is not null)
                {
                    AddEventLogEntry(message, isError: true);
                }
            });

    private void AddEventLogEntry(string message, bool isError = false)
    {
        while (EventLog.Count >= EventLogCapacity)
        {
            EventLog.RemoveAt(0);
        }

        EventLog.Add(new EventLogItem(DateTimeOffset.Now, message, isError));
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    private static string FormatStatus(ReviewServiceStatus status) => status switch
    {
        ReviewServiceStatus.Stopped => "Stopped",
        ReviewServiceStatus.WaitingForSimulator => "Waiting for iRacing",
        ReviewServiceStatus.Connected => "Connected to iRacing",
        ReviewServiceStatus.Unavailable => "iRacing unavailable",
        _ => "Unknown",
    };

    private static string FormatError(Error error) => error.Message;

    private static string FormatIncidentCount(int count) => count == 1
        ? "1 incident"
        : string.Create(CultureInfo.CurrentCulture, $"{count} incidents");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void RaiseCommandStates()
    {
        ((AsyncPresentationCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)OpenSessionCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)ReviewCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewIncidentCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)SavePreferencesCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)ToggleMonitoringCommand).RaiseCanExecuteChanged();
        ((PresentationCommand)CancelCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsEmptyState));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
