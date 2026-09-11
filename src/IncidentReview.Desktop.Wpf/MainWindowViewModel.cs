using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Runs UI effects and reduces their results into one immutable presentation state.</summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly ReplayOffset BeforeOffset =
        ReplayOffset.TryCreateMilliseconds(-2_000).Value;
    private static readonly ReplayOffset AfterOffset =
        ReplayOffset.TryCreateMilliseconds(2_000).Value;
    private static readonly Error InvalidCustomEventKeyError = Error.Create(
        ErrorCode.Define("desktop.global-shortcut.invalid-gesture"),
        ErrorKind.Validation,
        "Enter a valid shortcut such as F9 or Ctrl+Shift+F9.");

    private readonly IIncidentReviewService _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly IThemeController _themeController;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private MainWindowState _state;
    private bool _isPublishingState;
    private bool _isDisposed;

    public MainWindowViewModel(
        IIncidentReviewService service,
        IUiDispatcher dispatcher,
        IThemeController themeController)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _themeController = themeController ?? throw new ArgumentNullException(nameof(themeController));
        _state = MainWindowReducer.CreateInitialState(
            _themeController.Resolve(ThemePreference.FollowDesktop));
        _themeController.DesktopThemeChanged += OnDesktopThemeChanged;

        RefreshCommand = new AsyncPresentationCommand(
            RefreshAsync,
            () => !State.IsBusy);
        ReviewBeforeCommand = CreateReviewCommand(BeforeOffset);
        ReviewAtCommand = CreateReviewCommand(ReplayOffset.Zero);
        ReviewAfterCommand = CreateReviewCommand(AfterOffset);
        ReviewCustomEventBeforeCommand = CreateCustomEventReviewCommand(BeforeOffset);
        ReviewCustomEventAtCommand = CreateCustomEventReviewCommand(ReplayOffset.Zero);
        ReviewCustomEventAfterCommand = CreateCustomEventReviewCommand(AfterOffset);
        CreateCustomEventCommand = new AsyncPresentationCommand(
            CreateCustomEventAsync,
            () => !State.IsBusy && State.HasActiveSession);
        SavePreferencesCommand = new AsyncPresentationCommand(
            SavePreferencesAsync,
            () => !State.IsBusy && State.IsInitialized);
        SaveCustomEventSettingsCommand = new AsyncPresentationCommand(
            SaveCustomEventSettingsAsync,
            () => !State.IsBusy && State.IsInitialized);
        ToggleCustomEventSessionCommand = new AsyncPresentationCommand(
            ToggleCustomEventSessionAsync,
            () => !State.IsBusy && State.HasActiveSession);
        UseSharedEventServerCommand = new AsyncPresentationCommand(
            UseSharedEventServerAsync,
            () => !State.IsBusy && State.HasActiveSession &&
                !State.IsHostingCustomEventSession);
        CycleThemeCommand = new AsyncPresentationCommand(
            CycleThemeAsync,
            () => !State.IsBusy && State.IsInitialized);
        ToggleMonitoringCommand = new AsyncPresentationCommand(
            ToggleMonitoringAsync,
            () => !State.IsBusy);
        CancelCommand = new PresentationCommand(CancelActiveOperation, () => State.IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the sole immutable state tree rendered by the main window.</summary>
    public MainWindowState State => _state;

    public IncidentListItem? SelectedIncident
    {
        get => State.SelectedIncident;
        set
        {
            if (_isPublishingState)
            {
                return;
            }

            var incidentId = value?.Id;
            if (Equals(State.SelectedIncidentId, incidentId))
            {
                return;
            }

            ApplyAction(new MainWindowAction.SelectIncident(incidentId));
        }
    }

    public CameraChoice SelectedCameraChoice
    {
        get => State.SelectedCameraChoice;
        set
        {
            if (_isPublishingState || value is null)
            {
                return;
            }

            if (string.Equals(
                    State.SelectedCameraChoice.CameraName,
                    value.CameraName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyAction(new MainWindowAction.SelectCamera(value.CameraName));
        }
    }

    public string CustomEventSubmitter
    {
        get => State.CustomEventSubmitterDraft;
        set => EditCustomEventSettings(value, State.CustomEventKeyDraft, State.EventJoinCodeDraft);
    }

    public string CustomEventKey
    {
        get => State.CustomEventKeyDraft;
        set => EditCustomEventSettings(State.CustomEventSubmitterDraft, value, State.EventJoinCodeDraft);
    }

    public string EventJoinCode
    {
        get => State.EventJoinCodeDraft;
        set => EditCustomEventSettings(State.CustomEventSubmitterDraft, State.CustomEventKeyDraft, value);
    }

    public string EventHostAddress
    {
        get => State.EventHostAddressDraft;
        set
        {
            if (_isPublishingState || value is null ||
                string.Equals(value, State.EventHostAddressDraft, StringComparison.Ordinal))
            {
                return;
            }

            ApplyAction(new MainWindowAction.EditEventHostAddress(value));
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand ReviewBeforeCommand { get; }

    public ICommand ReviewAtCommand { get; }

    public ICommand ReviewAfterCommand { get; }

    public ICommand ReviewCustomEventBeforeCommand { get; }

    public ICommand ReviewCustomEventAtCommand { get; }

    public ICommand ReviewCustomEventAfterCommand { get; }

    public ICommand CreateCustomEventCommand { get; }

    public ICommand SavePreferencesCommand { get; }

    public ICommand SaveCustomEventSettingsCommand { get; }

    public ICommand ToggleCustomEventSessionCommand { get; }

    public ICommand UseSharedEventServerCommand { get; }

    public ICommand CycleThemeCommand { get; }

    public ICommand ToggleMonitoringCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>
    /// Seeds durable preferences and their resolved palette before the window is shown.
    /// </summary>
    public void PrepareForStartup(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ThrowIfDisposed();
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "Startup preferences must be applied on the WPF dispatcher thread.");
        }

        ApplyAction(new MainWindowAction.ApplyPreferences(
            preferences,
            _themeController.Resolve(preferences.Theme)));
    }

    /// <summary>Loads and applies durable preferences before the WPF message loop starts.</summary>
    internal Result PrepareStoredPreferencesForStartup(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "Startup preferences must be applied on the WPF dispatcher thread.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var preferences = _service
            .GetPreferencesAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!preferences.IsSuccess)
        {
            return Result.Failure(preferences.Error!);
        }

        PrepareForStartup(preferences.Value);
        return Result.Success();
    }

    /// <summary>Loads the first coherent snapshot, then observes application changes.</summary>
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

    /// <summary>Replaces the rendered application data with one coherent snapshot.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            token => RefreshSnapshotCoreAsync(SnapshotRefreshMode.Manual, token),
            cancellationToken);

    /// <summary>Requests replay review at a validated offset from one incident.</summary>
    public Task ReviewIncidentAsync(
        IncidentListItem incident,
        ReplayOffset offset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(offset);

        return RunUiOperationAsync(
            async token =>
            {
                var result = await _service.ReviewIncidentAsync(
                    incident.Id,
                    offset,
                    token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                var message = string.Create(
                    CultureInfo.CurrentCulture,
                    $"Replay ready for incident {incident.Id} ({FormatOffset(offset)}).");
                await ShowNoticeAsync(message, isError: false).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>Requests replay review at a validated offset from one custom event.</summary>
    public Task ReviewCustomEventAsync(
        CustomEventListItem customEvent,
        ReplayOffset offset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customEvent);
        ArgumentNullException.ThrowIfNull(offset);

        return RunUiOperationAsync(
            async token =>
            {
                var result = await _service.ReviewCustomEventAsync(
                    customEvent.Id,
                    offset,
                    token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                var message = string.Create(
                    CultureInfo.CurrentCulture,
                    $"Replay ready for custom event {customEvent.Id} ({FormatOffset(offset)}).");
                await ShowNoticeAsync(message, isError: false).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>Creates a local custom event at the latest live replay position.</summary>
    public Task CreateCustomEventAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                var result = await _service.CreateCustomEventAsync(token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                await RefreshSnapshotCoreAsync(
                    SnapshotRefreshMode.Automatic,
                    token).ConfigureAwait(false);
                await ShowNoticeAsync(
                    "Custom review event saved locally.",
                    isError: false).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Saves only the camera draft while preserving hidden durable replay behavior.</summary>
    public Task SavePreferencesAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                var saved = State.SavedPreferences;
                var preferences = UserPreferences.TryCreateMilliseconds(
                    saved.ReplayLeadInMilliseconds,
                    saved.PlaybackSpeed,
                    saved.AutoPause,
                    State.SelectedCameraChoice.CameraName,
                    saved.Theme,
                    saved.SubmitterName,
                    saved.CustomEventKey,
                    saved.EventJoinCode);
                if (!preferences.IsSuccess)
                {
                    await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
                    return;
                }

                var result = await _service.UpdatePreferencesAsync(
                    preferences.Value,
                    token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                var refreshed = await RefreshSnapshotCoreAsync(
                    SnapshotRefreshMode.Automatic,
                    token).ConfigureAwait(false);
                if (refreshed && State.StatusError is null)
                {
                    await ShowNoticeAsync(
                        "Camera preference saved.",
                        isError: false).ConfigureAwait(false);
                }
            },
            cancellationToken);

    /// <summary>Saves the submitter, shortcut, and optional server join code.</summary>
    public Task SaveCustomEventSettingsAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                var preferences = CreateCustomEventPreferences(State.EventJoinCodeDraft);
                if (!preferences.IsSuccess)
                {
                    await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
                    return;
                }

                var result = await _service.UpdatePreferencesAsync(
                    preferences.Value,
                    token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                    return;
                }

                var refreshed = await RefreshSnapshotCoreAsync(
                    SnapshotRefreshMode.Automatic,
                    token).ConfigureAwait(false);
                if (refreshed && State.StatusError is null)
                {
                    await ShowNoticeAsync(
                        string.IsNullOrWhiteSpace(preferences.Value.EventJoinCode)
                            ? "Custom events will be kept on this computer."
                            : "Custom-event server joined. Pending events will sync when you leave the car.",
                        isError: false).ConfigureAwait(false);
                }
            },
            cancellationToken);

    /// <summary>Cycles and durably saves Follow desktop, Light, and Dark in that order.</summary>
    public Task CycleThemeAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                var previousPreferences = State.SavedPreferences;
                var nextTheme = NextTheme(previousPreferences.Theme);
                var nextPreferences = UserPreferences.TryCreateMilliseconds(
                    previousPreferences.ReplayLeadInMilliseconds,
                    previousPreferences.PlaybackSpeed,
                    previousPreferences.AutoPause,
                    previousPreferences.PreferredCamera,
                    nextTheme,
                    previousPreferences.SubmitterName,
                    previousPreferences.CustomEventKey,
                    previousPreferences.EventJoinCode);
                if (!nextPreferences.IsSuccess)
                {
                    await ShowErrorAsync(nextPreferences.Error!).ConfigureAwait(false);
                    return;
                }

                await ApplyPreferencesOnUiAsync(nextPreferences.Value).ConfigureAwait(false);

                var persisted = false;
                try
                {
                    var result = await _service.UpdatePreferencesAsync(
                        nextPreferences.Value,
                        token).ConfigureAwait(false);
                    if (!result.IsSuccess)
                    {
                        await ApplyPreferencesOnUiAsync(previousPreferences).ConfigureAwait(false);
                        await ShowErrorAsync(result.Error!).ConfigureAwait(false);
                        return;
                    }

                    persisted = true;
                    var refreshed = await RefreshSnapshotCoreAsync(
                        SnapshotRefreshMode.Automatic,
                        token).ConfigureAwait(false);
                    if (refreshed && State.StatusError is null)
                    {
                        await ShowNoticeAsync(
                            "Theme preference saved.",
                            isError: false).ConfigureAwait(false);
                    }
                }
                catch
                {
                    if (!persisted)
                    {
                        await ApplyPreferencesOnUiAsync(previousPreferences).ConfigureAwait(false);
                    }

                    throw;
                }
            },
            cancellationToken);

    /// <summary>Starts observing application-owned state-change notifications.</summary>
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
        await ApplyActionOnUiAsync(
            new MainWindowAction.SetMonitoring(
                isMonitoring: true,
                DateTimeOffset.Now)).ConfigureAwait(false);
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
            if (!_isDisposed)
            {
                await ApplyActionOnUiAsync(
                    new MainWindowAction.SetMonitoring(
                        isMonitoring: false,
                        DateTimeOffset.Now)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Cancels the current user operation.</summary>
    public void CancelActiveOperation() => _activeOperationCancellation?.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _themeController.DesktopThemeChanged -= OnDesktopThemeChanged;
        _lifetimeCancellation.Cancel();
        _activeOperationCancellation?.Cancel();
        await StopMonitoringAsync().ConfigureAwait(false);

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _operationGate.Release();
        _lifetimeCancellation.Dispose();
        _operationGate.Dispose();
    }

    private AsyncPresentationCommand<IncidentListItem> CreateReviewCommand(ReplayOffset offset) =>
        new(
            (incident, token) => ReviewIncidentAsync(incident, offset, token),
            incident => !State.IsBusy && State.Incidents.Any(item => item.Id == incident.Id));

    private AsyncPresentationCommand<CustomEventListItem> CreateCustomEventReviewCommand(
        ReplayOffset offset) => new(
            (customEvent, token) => ReviewCustomEventAsync(customEvent, offset, token),
            customEvent => !State.IsBusy &&
                State.CustomEvents.Any(item => item.Id == customEvent.Id));

    private async Task ToggleCustomEventSessionAsync(CancellationToken cancellationToken)
    {
        if (State.IsHostingCustomEventSession)
        {
            await StopCustomEventSessionAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StartCustomEventSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task UseSharedEventServerAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                if (!Uri.TryCreate(
                        State.EventHostAddressDraft.Trim(),
                        UriKind.Absolute,
                        out var serverBaseUri))
                {
                    await ShowNoticeAsync(
                        "Enter a complete HTTP server address, such as http://review.example:5088/.",
                        isError: true).ConfigureAwait(false);
                    return;
                }

                var created = await _service.CreateCustomEventJoinCodeAsync(
                    serverBaseUri,
                    token).ConfigureAwait(false);
                if (!created.IsSuccess)
                {
                    await ShowErrorAsync(created.Error!).ConfigureAwait(false);
                    return;
                }

                var preferences = CreateCustomEventPreferences(created.Value);
                if (!preferences.IsSuccess)
                {
                    await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
                    return;
                }

                var saved = await _service.UpdatePreferencesAsync(
                    preferences.Value,
                    token).ConfigureAwait(false);
                if (!saved.IsSuccess)
                {
                    await ShowErrorAsync(saved.Error!).ConfigureAwait(false);
                    return;
                }

                await RefreshSnapshotCoreAsync(
                    SnapshotRefreshMode.Automatic,
                    token).ConfigureAwait(false);
                await ShowNoticeAsync(
                    "Shared-server join code created. Copy it for the other drivers.",
                    isError: false).ConfigureAwait(false);
            },
            cancellationToken);

    private Task StartCustomEventSessionAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                if (!Uri.TryCreate(
                        State.EventHostAddressDraft.Trim(),
                        UriKind.Absolute,
                        out var hostAddress))
                {
                    await ShowNoticeAsync(
                        "Enter a complete HTTP server address, such as http://localhost:5088/.",
                        isError: true).ConfigureAwait(false);
                    return;
                }

                var started = await _service.StartCustomEventSessionAsync(
                    hostAddress,
                    hostAddress,
                    token).ConfigureAwait(false);
                if (!started.IsSuccess)
                {
                    await ShowErrorAsync(started.Error!).ConfigureAwait(false);
                    return;
                }

                var keepHosting = false;
                try
                {
                    var preferences = CreateCustomEventPreferences(started.Value);
                    if (!preferences.IsSuccess)
                    {
                        await ShowErrorAsync(preferences.Error!).ConfigureAwait(false);
                        return;
                    }

                    var saved = await _service.UpdatePreferencesAsync(
                        preferences.Value,
                        token).ConfigureAwait(false);
                    if (!saved.IsSuccess)
                    {
                        await ShowErrorAsync(saved.Error!).ConfigureAwait(false);
                        return;
                    }

                    await ApplyActionOnUiAsync(
                        new MainWindowAction.SetCustomEventSessionHosting(isHosting: true))
                        .ConfigureAwait(false);
                    keepHosting = true;
                    await RefreshSnapshotCoreAsync(
                        SnapshotRefreshMode.Automatic,
                        token).ConfigureAwait(false);
                    await ShowNoticeAsync(
                        "Joinable custom-event session started. Share the join code shown in Advanced.",
                        isError: false).ConfigureAwait(false);
                }
                finally
                {
                    if (!keepHosting)
                    {
                        var stopped = await _service
                            .StopCustomEventSessionAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        if (!stopped.IsSuccess)
                        {
                            await ApplyActionOnUiAsync(
                                new MainWindowAction.SetCustomEventSessionHosting(
                                    isHosting: true))
                                .ConfigureAwait(false);
                            await ShowErrorAsync(stopped.Error!).ConfigureAwait(false);
                        }
                    }
                }
            },
            cancellationToken);

    private Task StopCustomEventSessionAsync(CancellationToken cancellationToken) =>
        RunUiOperationAsync(
            async token =>
            {
                var stopped = await _service.StopCustomEventSessionAsync(token).ConfigureAwait(false);
                if (!stopped.IsSuccess)
                {
                    await ShowErrorAsync(stopped.Error!).ConfigureAwait(false);
                    return;
                }

                await ApplyActionOnUiAsync(
                    new MainWindowAction.SetCustomEventSessionHosting(isHosting: false))
                    .ConfigureAwait(false);
                await ShowNoticeAsync(
                    "Joinable custom-event session stopped.",
                    isError: false).ConfigureAwait(false);
            },
            cancellationToken);

    private Result<UserPreferences> CreateCustomEventPreferences(string joinCode)
    {
        if (!GlobalShortcutGestureParser.TryParse(State.CustomEventKeyDraft, out _))
        {
            return Result<UserPreferences>.Failure(InvalidCustomEventKeyError);
        }

        var submitter = SubmitterName.TryCreate(State.CustomEventSubmitterDraft);
        if (!submitter.IsSuccess)
        {
            return Result<UserPreferences>.Failure(submitter.Error!);
        }

        var saved = State.SavedPreferences;
        return UserPreferences.TryCreateMilliseconds(
            saved.ReplayLeadInMilliseconds,
            saved.PlaybackSpeed,
            saved.AutoPause,
            saved.PreferredCamera,
            saved.Theme,
            submitter.Value.Value,
            State.CustomEventKeyDraft,
            string.IsNullOrWhiteSpace(joinCode) ? null : joinCode);
    }

    private void EditCustomEventSettings(string submitterName, string key, string joinCode)
    {
        if (_isPublishingState || submitterName is null || key is null || joinCode is null)
        {
            return;
        }

        ApplyAction(new MainWindowAction.EditCustomEventSettings(
            submitterName,
            key,
            joinCode));
    }

    private async Task ToggleMonitoringAsync(CancellationToken cancellationToken)
    {
        if (State.IsMonitoring)
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
            await foreach (var update in _service
                .ObserveUpdatesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await RunUiOperationAsync(
                    async token =>
                    {
                        await ShowUpdateNoticeAsync(update).ConfigureAwait(false);
                        await RefreshSnapshotCoreAsync(
                            SnapshotRefreshMode.Automatic,
                            token).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await ApplyActionOnUiAsync(
                new MainWindowAction.SetMonitoring(
                    isMonitoring: false,
                    DateTimeOffset.Now)).ConfigureAwait(false);
            await ShowNoticeAsync(
                "Live updates stopped unexpectedly. Use Refresh or restart monitoring.",
                isError: true).ConfigureAwait(false);
        }
    }

    private Task ShowUpdateNoticeAsync(ReviewUpdate update) => update switch
    {
        ReviewUpdate.IncidentChanged incident => ShowNoticeAsync(
            string.Create(
                CultureInfo.CurrentCulture,
                $"Incident recorded: {incident.Incident}."),
            isError: false),
        ReviewUpdate.SessionChanged session => ShowNoticeAsync(
            string.Create(
                CultureInfo.CurrentCulture,
                $"Session changed: {session.Session}."),
            isError: false),
        ReviewUpdate.CustomEventChanged customEvent => ShowNoticeAsync(
            string.Create(
                CultureInfo.CurrentCulture,
                $"Custom event updated: {customEvent.CustomEvent}."),
            isError: false),
        ReviewUpdate.SynchronizationWarning warning => ShowErrorAsync(warning.Error),
        _ => Task.CompletedTask,
    };

    internal Task ReportShortcutErrorAsync(Error error) => ShowErrorAsync(error);

    private async Task<bool> RefreshSnapshotCoreAsync(
        SnapshotRefreshMode refreshMode,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            await ShowErrorAsync(result.Error!).ConfigureAwait(false);
            return false;
        }

        await ApplySnapshotOnUiAsync(
            result.Value,
            refreshMode,
            DateTimeOffset.Now).ConfigureAwait(false);
        return true;
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
            await ApplyActionOnUiAsync(new MainWindowAction.SetBusy(isBusy: true)).ConfigureAwait(false);
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
            await ApplyActionOnUiAsync(new MainWindowAction.SetBusy(isBusy: false)).ConfigureAwait(false);
            _operationGate.Release();
        }
    }

    private Task ShowErrorAsync(Error error) => ShowNoticeAsync(error.Message, isError: true);

    private Task ShowNoticeAsync(string message, bool isError) => ApplyActionOnUiAsync(
        new MainWindowAction.ShowNotice(DateTimeOffset.Now, message, isError));

    private Task ApplyActionOnUiAsync(MainWindowAction action)
        => InvokeOnUiAsync(() => ApplyAction(action));

    private Task ApplyPreferencesOnUiAsync(UserPreferences preferences) => InvokeOnUiAsync(
        () => ApplyAction(new MainWindowAction.ApplyPreferences(
            preferences,
            _themeController.Resolve(preferences.Theme))));

    private Task ApplySnapshotOnUiAsync(
        ReviewSnapshot snapshot,
        SnapshotRefreshMode refreshMode,
        DateTimeOffset occurredAt) => InvokeOnUiAsync(
            () => ApplyAction(new MainWindowAction.ApplySnapshot(
                snapshot,
                _themeController.Resolve(snapshot.Preferences.Theme),
                refreshMode,
                occurredAt)));

    private Task InvokeOnUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    private void ApplyAction(MainWindowAction action)
    {
        var next = MainWindowReducer.Reduce(_state, action);
        if (ReferenceEquals(next, _state))
        {
            return;
        }

        var previous = _state;
        _state = next;
        _isPublishingState = true;
        try
        {
            OnPropertyChanged(nameof(State));
            if (!ReferenceEquals(previous.Incidents, next.Incidents) ||
                !Equals(previous.SelectedIncidentId, next.SelectedIncidentId))
            {
                OnPropertyChanged(nameof(SelectedIncident));
            }

            if (!ReferenceEquals(previous.CameraChoices, next.CameraChoices) ||
                !ReferenceEquals(previous.SelectedCameraChoice, next.SelectedCameraChoice))
            {
                OnPropertyChanged(nameof(SelectedCameraChoice));
            }

            if (!string.Equals(
                    previous.CustomEventSubmitterDraft,
                    next.CustomEventSubmitterDraft,
                    StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(CustomEventSubmitter));
            }

            if (!string.Equals(
                    previous.CustomEventKeyDraft,
                    next.CustomEventKeyDraft,
                    StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(CustomEventKey));
            }

            if (!string.Equals(
                    previous.EventJoinCodeDraft,
                    next.EventJoinCodeDraft,
                    StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(EventJoinCode));
            }

            if (!string.Equals(
                    previous.EventHostAddressDraft,
                    next.EventHostAddressDraft,
                    StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(EventHostAddress));
            }

            RaiseCommandStates();
        }
        finally
        {
            _isPublishingState = false;
        }
    }

    private void RaiseCommandStates()
    {
        ((AsyncPresentationCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewBeforeCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewAtCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewAfterCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<CustomEventListItem>)ReviewCustomEventBeforeCommand)
            .RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<CustomEventListItem>)ReviewCustomEventAtCommand)
            .RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<CustomEventListItem>)ReviewCustomEventAfterCommand)
            .RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)CreateCustomEventCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)SavePreferencesCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)SaveCustomEventSettingsCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)ToggleCustomEventSessionCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)UseSharedEventServerCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)CycleThemeCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)ToggleMonitoringCommand).RaiseCanExecuteChanged();
        ((PresentationCommand)CancelCommand).RaiseCanExecuteChanged();
    }

    private static string FormatOffset(ReplayOffset offset) => offset.Milliseconds switch
    {
        < 0 => string.Create(
            CultureInfo.CurrentCulture,
            $"{Math.Abs(offset.Milliseconds) / 1_000d:G} sec before"),
        > 0 => string.Create(
            CultureInfo.CurrentCulture,
            $"{offset.Milliseconds / 1_000d:G} sec after"),
        _ => "event time",
    };

    private static ThemePreference NextTheme(ThemePreference current) => current switch
    {
        ThemePreference.FollowDesktop => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        ThemePreference.Dark => ThemePreference.FollowDesktop,
        _ => throw new ArgumentOutOfRangeException(nameof(current), current, "Unknown theme preference."),
    };

    private async void OnDesktopThemeChanged(object? sender, ResolvedThemeChangedEventArgs e)
    {
        _ = sender;
        if (_isDisposed)
        {
            return;
        }

        try
        {
            await ApplyActionOnUiAsync(
                new MainWindowAction.DesktopThemeChanged(e.Theme)).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (_isDisposed)
        {
        }
        catch (TaskCanceledException) when (_isDisposed)
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
