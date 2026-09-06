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

    private readonly IIncidentReviewService _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private MainWindowState _state = MainWindowReducer.InitialState;
    private bool _isDisposed;

    public MainWindowViewModel(IIncidentReviewService service, IUiDispatcher dispatcher)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        RefreshCommand = new AsyncPresentationCommand(
            RefreshAsync,
            () => !State.IsBusy);
        ReviewBeforeCommand = CreateReviewCommand(BeforeOffset);
        ReviewAtCommand = CreateReviewCommand(ReplayOffset.Zero);
        ReviewAfterCommand = CreateReviewCommand(AfterOffset);
        SavePreferencesCommand = new AsyncPresentationCommand(
            SavePreferencesAsync,
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
        set => ApplyAction(new MainWindowAction.SelectIncident(value?.Id));
    }

    public CameraChoice SelectedCameraChoice
    {
        get => State.SelectedCameraChoice;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ApplyAction(new MainWindowAction.SelectCamera(value.CameraName));
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand ReviewBeforeCommand { get; }

    public ICommand ReviewAtCommand { get; }

    public ICommand ReviewAfterCommand { get; }

    public ICommand SavePreferencesCommand { get; }

    public ICommand ToggleMonitoringCommand { get; }

    public ICommand CancelCommand { get; }

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
                    State.SelectedCameraChoice.CameraName);
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
            await ApplyActionOnUiAsync(
                new MainWindowAction.SetMonitoring(
                    isMonitoring: false,
                    DateTimeOffset.Now)).ConfigureAwait(false);
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
        _ => Task.CompletedTask,
    };

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

        await ApplyActionOnUiAsync(
            new MainWindowAction.ApplySnapshot(
                result.Value,
                refreshMode,
                DateTimeOffset.Now)).ConfigureAwait(false);
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
    {
        if (_dispatcher.CheckAccess())
        {
            ApplyAction(action);
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(() => ApplyAction(action));
    }

    private void ApplyAction(MainWindowAction action)
    {
        var next = MainWindowReducer.Reduce(_state, action);
        if (ReferenceEquals(next, _state))
        {
            return;
        }

        _state = next;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(SelectedIncident));
        OnPropertyChanged(nameof(SelectedCameraChoice));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        ((AsyncPresentationCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewBeforeCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewAtCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand<IncidentListItem>)ReviewAfterCommand).RaiseCanExecuteChanged();
        ((AsyncPresentationCommand)SavePreferencesCommand).RaiseCanExecuteChanged();
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
