using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.EventSync.Contracts;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Application;

internal sealed class IncidentReviewApplication :
    IIncidentReviewService,
    IApplicationRuntime,
    ICustomEventReceiver,
    IDisposable
{
    private static readonly TimeSpan InitialPersistenceRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumPersistenceRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly ReplayContext EmptyReplayContext =
        ReplayContext.TryCreate(null, [], null).Value;

    private readonly ITelemetrySource _telemetrySource;
    private readonly IReplayController _replayController;
    private readonly IReplayContextReader _replayContextReader;
    private readonly IStore _store;
    private readonly ICustomEventPublisher _customEventPublisher;
    private readonly ICustomEventSessionHost _customEventSessionHost;
    private readonly ICurrentTelemetryReader? _currentTelemetryReader;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateLock = new();
    private readonly Channel<ReviewUpdate> _updates = Channel.CreateUnbounded<ReviewUpdate>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Channel<byte> _outboxFlushRequests = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? _cancelWorker;
    private Task? _worker;
    private Task _customEventHostObservation = Task.CompletedTask;
    private Exception? _terminalException;
    private bool _stopRequested;
    private bool _reviewCommandInProgress;
    private bool _ownedReviewActive;
    private long _snapshotRevision;
    private ReviewServiceStatus _status = ReviewServiceStatus.Stopped;
    private OnTrackState _onTrackState = OnTrackState.Unknown;
    private Error? _lastUnavailableError;
    private SessionIdentity? _currentSession;
    private StoredSession? _activeSession;
    private SimulatorSessionDescriptor? _replayDescriptor;
    private readonly Dictionary<ParticipantIdentity, IncidentCheckpoint> _checkpoints = [];
    private PendingStoreTransition? _pendingTransition;
    private TelemetrySample? _latestLiveSample;
    private readonly SemaphoreSlim _receivedEventGate = new(1, 1);
    private CancellationTokenSource? _activeOutboxFlushCancellation;
    private bool _outboxFlushSafetyConfirmed;
    private bool _outboxFlushAttemptedForOffTrackPeriod;

    public IncidentReviewApplication(
        ITelemetrySource telemetrySource,
        IReplayController replayController,
        IReplayContextReader replayContextReader,
        IStore store,
        ICustomEventPublisher customEventPublisher,
        ICustomEventSessionHost customEventSessionHost,
        TimeProvider timeProvider,
        ICurrentTelemetryReader? currentTelemetryReader = null)
    {
        _telemetrySource = telemetrySource;
        _replayController = replayController;
        _replayContextReader = replayContextReader;
        _store = store;
        _customEventPublisher = customEventPublisher;
        _customEventSessionHost = customEventSessionHost;
        _timeProvider = timeProvider;
        _currentTelemetryReader = currentTelemetryReader;
    }

    public Task Completion => _completion.Task;

    public Task<Result> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_stopRequested)
            {
                return Task.FromResult(Result.Failure(ApplicationErrors.RuntimeStopped));
            }

            if (_worker is not null)
            {
                return Task.FromResult(Result.Success());
            }

            _status = ReviewServiceStatus.WaitingForSimulator;
            var lifetime = new CancellationTokenSource();
            _cancelWorker = lifetime.Cancel;
            _worker = Task.Run(
                () => RunWithLifetimeAsync(lifetime),
                CancellationToken.None);
        }

        Publish(ReviewUpdate.StatusChanged.Create(ReviewServiceStatus.WaitingForSimulator));
        return Task.FromResult(Result.Success());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? worker;
        Action? cancelWorker;
        lock (_stateLock)
        {
            _stopRequested = true;
            worker = _worker;
            cancelWorker = _cancelWorker;
            if (worker is null)
            {
                _status = ReviewServiceStatus.Stopped;
                _completion.TrySetResult();
                _updates.Writer.TryComplete();
            }
        }

        cancelWorker?.Invoke();

        if (worker is not null)
        {
            await worker.ConfigureAwait(false);
        }

        _ = await _customEventSessionHost.StopAsync(cancellationToken).ConfigureAwait(false);
        Task hostObservation;
        lock (_stateLock)
        {
            hostObservation = _customEventHostObservation;
        }

        await hostObservation.ConfigureAwait(false);

        if (_terminalException is not null)
        {
            ExceptionDispatchInfo.Capture(_terminalException).Throw();
        }
    }

    public Task<Result<ReviewServiceStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            return Task.FromResult(Result<ReviewServiceStatus>.Success(_status));
        }
    }

    public async Task<Result<ReviewSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var anchor = CaptureSnapshotAnchor();

            ReviewSession? activeSession = null;
            if (anchor.CurrentSession is not null)
            {
                var sessionResult = await _store.QueryAsync(
                    new GetSessionDetails(anchor.CurrentSession),
                    cancellationToken).ConfigureAwait(false);
                if (!sessionResult.IsSuccess)
                {
                    if (!IsSnapshotAnchorCurrent(anchor))
                    {
                        continue;
                    }

                    return Result<ReviewSnapshot>.Failure(sessionResult.Error!);
                }

                if (!sessionResult.Value.IsFound)
                {
                    if (!IsSnapshotAnchorCurrent(anchor))
                    {
                        continue;
                    }

                    return Result<ReviewSnapshot>.Failure(ApplicationErrors.SessionNotFound);
                }

                activeSession = MapSession(sessionResult.Value.Value!);
            }

            var preferences = await _store.QueryAsync(GetPreferences.Instance, cancellationToken)
                .ConfigureAwait(false);
            if (!preferences.IsSuccess)
            {
                if (!IsSnapshotAnchorCurrent(anchor))
                {
                    continue;
                }

                return Result<ReviewSnapshot>.Failure(preferences.Error!);
            }

            var exposeReplayContext = anchor.Status is
                ReviewServiceStatus.Connected or ReviewServiceStatus.Unavailable;
            var replayContextResult = exposeReplayContext
                ? _replayContextReader.Read()
                : Result<ReplayContext>.Success(EmptyReplayContext);

            if (!IsSnapshotAnchorCurrent(anchor))
            {
                continue;
            }

            var context = replayContextResult.IsSuccess
                ? replayContextResult.Value
                : EmptyReplayContext;
            return Result<ReviewSnapshot>.Success(ReviewSnapshot.Create(
                anchor.Revision,
                anchor.Status,
                anchor.StatusError,
                activeSession,
                preferences.Value,
                context.DriverDisplayName,
                context.CameraGroups,
                context.CurrentCameraGroup));
        }
    }

    public async Task<Result<ReviewSession>> GetCurrentSessionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionIdentity? session;
        lock (_stateLock)
        {
            session = _currentSession;
        }

        return session is null
            ? Result<ReviewSession>.Failure(ApplicationErrors.NoCurrentSession)
            : await GetSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await _store.QueryAsync(ListSessions.Instance, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<SessionSummary>>.Failure(result.Error!);
        }

        IReadOnlyList<SessionSummary> summaries = Array.AsReadOnly(result.Value
            .Select(static item => SessionSummary.Create(
                item.Session.Id,
                item.Session.Descriptor,
                item.Session.StartedAt,
                item.IncidentCount,
                item.PendingIncidentCount))
            .ToArray());
        return Result<IReadOnlyList<SessionSummary>>.Success(summaries);
    }

    public async Task<Result<ReviewSession>> GetSessionAsync(
        SessionIdentity session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await _store.QueryAsync(
            new GetSessionDetails(session),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result<ReviewSession>.Failure(result.Error!);
        }

        return result.Value.IsFound
            ? Result<ReviewSession>.Success(MapSession(result.Value.Value!))
            : Result<ReviewSession>.Failure(ApplicationErrors.SessionNotFound);
    }

    public async Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        ReplayOffset offset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incidentId);
        ArgumentNullException.ThrowIfNull(offset);
        return await ReviewIncidentCoreAsync(incidentId, offset, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incidentId);
        return ReviewIncidentCoreAsync(incidentId, offset: null, cancellationToken);
    }

    private async Task<Result> ReviewIncidentCoreAsync(
        IncidentId incidentId,
        ReplayOffset? offset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            var preconditionError = GetReplayPreconditionError();
            if (preconditionError is not null)
            {
                return Result.Failure(preconditionError);
            }

            if (_reviewCommandInProgress)
            {
                return Result.Failure(ApplicationErrors.ReplayCommandInProgress);
            }

            _reviewCommandInProgress = true;
        }

        try
        {
            var incident = await _store.QueryAsync(new GetIncident(incidentId), cancellationToken)
                .ConfigureAwait(false);
            if (!incident.IsSuccess)
            {
                return Result.Failure(incident.Error!);
            }

            if (!incident.Value.IsFound)
            {
                return Result.Failure(ApplicationErrors.IncidentNotFound);
            }

            var stored = incident.Value.Value!;
            return await ExecuteReplayReviewAsync(
                stored.Session,
                stored.Position,
                stored.Participant,
                offset,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                _reviewCommandInProgress = false;
            }
        }
    }

    public async Task<Result> ReviewCustomEventAsync(
        CustomEventId customEventId,
        ReplayOffset offset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customEventId);
        ArgumentNullException.ThrowIfNull(offset);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            var preconditionError = GetReplayPreconditionError();
            if (preconditionError is not null)
            {
                return Result.Failure(preconditionError);
            }

            if (_reviewCommandInProgress)
            {
                return Result.Failure(ApplicationErrors.ReplayCommandInProgress);
            }

            _reviewCommandInProgress = true;
        }

        try
        {
            var customEvent = await _store.QueryAsync(
                new GetCustomEvent(customEventId),
                cancellationToken).ConfigureAwait(false);
            if (!customEvent.IsSuccess)
            {
                return Result.Failure(customEvent.Error!);
            }

            if (!customEvent.Value.IsFound)
            {
                return Result.Failure(ApplicationErrors.CustomEventNotFound);
            }

            var stored = customEvent.Value.Value!;
            return await ExecuteReplayReviewAsync(
                stored.Session,
                stored.Position,
                null,
                offset,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                _reviewCommandInProgress = false;
            }
        }
    }

    private async Task<Result> ExecuteReplayReviewAsync(
        SessionIdentity session,
        ReplayPosition position,
        IncidentParticipant? participant,
        ReplayOffset? offset,
        CancellationToken cancellationToken)
    {
        var reviewArmed = false;
        var seekAccepted = false;
        try
        {
            var preferences = await _store.QueryAsync(GetPreferences.Instance, cancellationToken)
                .ConfigureAwait(false);
            if (!preferences.IsSuccess)
            {
                return Result.Failure(preferences.Error!);
            }

            var effectiveOffset = offset ?? ReplayOffset.TryCreateMilliseconds(
                -preferences.Value.ReplayLeadInMilliseconds).Value;
            var targetTime = effectiveOffset.ApplyTo(position.SessionTime);
            if (!targetTime.IsSuccess)
            {
                return Result.Failure(targetTime.Error!);
            }

            var target = ReplayPosition.TryCreate(position.SessionNumber, targetTime.Value).Value;
            var playback = preferences.Value.AutoPause
                ? ReplayPlayback.Paused
                : ReplayPlayback.TryCreatePlaying(preferences.Value.PlaybackSpeed).Value;
            var validation = _replayController.ValidatePlayback(playback);
            if (!validation.IsSuccess)
            {
                return validation;
            }

            lock (_stateLock)
            {
                var preconditionError = GetReplayPreconditionError(
                    session,
                    position.SessionNumber);
                if (preconditionError is not null)
                {
                    return Result.Failure(preconditionError);
                }

                _ownedReviewActive = true;
                reviewArmed = true;
            }

            var seek = await _replayController.SeekAsync(target, cancellationToken)
                .ConfigureAwait(false);
            if (!seek.IsSuccess)
            {
                return seek;
            }

            seekAccepted = true;
            if (participant is not null)
            {
                var focus = await _replayController.FocusParticipantAsync(
                        participant,
                        preferences.Value.PreferredCamera,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!focus.IsSuccess)
                {
                    return focus;
                }
            }

            return await _replayController.SetPlaybackAsync(playback, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (reviewArmed && !seekAccepted)
            {
                lock (_stateLock)
                {
                    _ownedReviewActive = false;
                }
            }
        }
    }

    private Error? GetReplayPreconditionError(
        SessionIdentity? requiredSession = null,
        SessionNumber? requiredSessionNumber = null)
    {
        if (_status == ReviewServiceStatus.Stopped)
        {
            return ApplicationErrors.RuntimeStopped;
        }

        if (_status == ReviewServiceStatus.WaitingForSimulator)
        {
            return ApplicationErrors.ReplayUnavailable;
        }

        if (_status == ReviewServiceStatus.Unavailable)
        {
            return _lastUnavailableError ?? ApplicationErrors.ReplayUnavailable;
        }

        if (_onTrackState == OnTrackState.OnTrack)
        {
            return ApplicationErrors.ReplayDriverOnTrack;
        }

        if (_onTrackState == OnTrackState.Unknown)
        {
            return ApplicationErrors.ReplayOnTrackStateUnknown;
        }

        if (requiredSession is null || _currentSession == requiredSession)
        {
            return null;
        }

        return _currentSession is null &&
            _replayDescriptor?.IdentityScope == SimulatorIdentityScope.ConnectionScoped &&
            _replayDescriptor.SessionNumber == requiredSessionNumber
                ? ApplicationErrors.ReplaySessionIdentityUnavailable
                : ApplicationErrors.ReplaySessionNotLoaded;
    }

    public async Task<Result> AnnotateIncidentAsync(
        IncidentId incidentId,
        IncidentAnnotation annotation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incidentId);
        ArgumentNullException.ThrowIfNull(annotation);
        var existing = await _store.QueryAsync(new GetIncident(incidentId), cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return Result.Failure(existing.Error!);
        }

        if (!existing.Value.IsFound)
        {
            return Result.Failure(ApplicationErrors.IncidentNotFound);
        }

        var command = AnnotateIncident.Create(incidentId, annotation, Now());
        var result = await ExecuteWithReconciliationAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            Publish(new ReviewUpdate.IncidentChanged(existing.Value.Value!.Session, incidentId));
        }

        return result;
    }

    public async Task<Result> CreateCustomEventAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoredSession? session;
        TelemetrySample? sample;
        lock (_stateLock)
        {
            session = _activeSession;
            sample = _latestLiveSample;
        }

        if (_currentTelemetryReader is not null)
        {
            var current = _currentTelemetryReader.Read();
            if (!current.IsSuccess)
            {
                return Result.Failure(ApplicationErrors.CustomEventUnavailable);
            }

            sample = current.Value;
        }

        if (session is null || sample is null || sample.Session.Mode != SessionMode.Live ||
            sample.Session != session.Descriptor)
        {
            return Result.Failure(ApplicationErrors.CustomEventUnavailable);
        }

        var preferences = await _store.QueryAsync(GetPreferences.Instance, cancellationToken)
            .ConfigureAwait(false);
        if (!preferences.IsSuccess)
        {
            return Result.Failure(preferences.Error!);
        }

        var submitter = SubmitterName.TryCreate(preferences.Value.SubmitterName);
        if (!submitter.IsSuccess)
        {
            return Result.Failure(ApplicationErrors.CustomEventNameRequired);
        }

        var customEvent = StoredCustomEvent.CreatePending(
            session.Id,
            sample.Position,
            submitter.Value,
            Now());
        var command = RecordCustomEvent.TryCreate(customEvent);
        if (!command.IsSuccess)
        {
            return Result.Failure(command.Error!);
        }

        var result = await ExecuteWithReconciliationAsync(command.Value, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            Publish(new ReviewUpdate.CustomEventChanged(session.Id, customEvent.Id));
            if (sample.OnTrackState == OnTrackState.NotOnTrack)
            {
                RequestOutboxFlush();
            }
        }

        return result;
    }

    public async Task<Result<string>> StartCustomEventSessionAsync(
        Uri listenUri,
        Uri advertisedBaseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listenUri);
        ArgumentNullException.ThrowIfNull(advertisedBaseUri);
        SessionIdentity? session;
        lock (_stateLock)
        {
            session = _currentSession;
        }

        if (session is null)
        {
            return Result<string>.Failure(ApplicationErrors.NoCurrentSession);
        }

        var request = CustomEventSessionHostRequest.TryCreate(
            session,
            listenUri,
            advertisedBaseUri);
        if (!request.IsSuccess)
        {
            return Result<string>.Failure(request.Error!);
        }

        var started = await _customEventSessionHost.StartAsync(
            request.Value,
            this,
            cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            return Result<string>.Failure(started.Error!);
        }

        var hostObservation = ObserveCustomEventHostAsync(
            _customEventSessionHost.Completion);
        lock (_stateLock)
        {
            _customEventHostObservation = hostObservation;
        }

        return Result<string>.Success(started.Value.Value);
    }

    public Task<Result<string>> CreateCustomEventJoinCodeAsync(
        Uri serverBaseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        cancellationToken.ThrowIfCancellationRequested();
        SessionIdentity? session;
        lock (_stateLock)
        {
            session = _currentSession;
        }

        if (session is null)
        {
            return Task.FromResult(Result<string>.Failure(ApplicationErrors.NoCurrentSession));
        }

        var joinCode = JoinCode.TryCreate(serverBaseUri, session);
        return Task.FromResult(joinCode.IsSuccess
            ? Result<string>.Success(joinCode.Value.Value)
            : Result<string>.Failure(joinCode.Error!));
    }

    public async Task<Result> StopCustomEventSessionAsync(CancellationToken cancellationToken)
    {
        var stopped = await _customEventSessionHost.StopAsync(cancellationToken)
            .ConfigureAwait(false);
        Task observation;
        lock (_stateLock)
        {
            observation = _customEventHostObservation;
        }

        await observation.ConfigureAwait(false);
        return stopped;
    }

    public async Task<Result<CustomEventPublishOutcome>> ReceiveAsync(
        CustomEventSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        await _receivedEventGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _store.QueryAsync(
                new GetSession(submission.SessionIdentity),
                cancellationToken).ConfigureAwait(false);
            if (!session.IsSuccess)
            {
                return Result<CustomEventPublishOutcome>.Failure(session.Error!);
            }

            if (!session.Value.IsFound)
            {
                return Result<CustomEventPublishOutcome>.Failure(ApplicationErrors.SessionNotFound);
            }

            var existing = await _store.QueryAsync(
                new GetCustomEvent(submission.Id),
                cancellationToken).ConfigureAwait(false);
            if (!existing.IsSuccess)
            {
                return Result<CustomEventPublishOutcome>.Failure(existing.Error!);
            }

            if (existing.Value.IsFound)
            {
                Publish(new ReviewUpdate.SynchronizationWarning(
                    ApplicationErrors.CustomEventDuplicate));
                return Result<CustomEventPublishOutcome>.Success(
                    CustomEventPublishOutcome.Duplicate);
            }

            var receivedAt = Now();
            var synchronizedAt = receivedAt.UnixMilliseconds >= submission.OccurredAt.UnixMilliseconds
                ? receivedAt
                : submission.OccurredAt;
            var stored = StoredCustomEvent.Create(
                submission.Id,
                submission.SessionIdentity,
                submission.ReplayPosition,
                submission.Submitter,
                submission.OccurredAt,
                CustomEventSynchronization.Synchronized.Create(synchronizedAt));
            var record = RecordReceivedCustomEvent.TryCreate(stored);
            if (!record.IsSuccess)
            {
                return Result<CustomEventPublishOutcome>.Failure(record.Error!);
            }

            var recorded = await ExecuteWithReconciliationAsync(record.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!recorded.IsSuccess)
            {
                if (recorded.Error!.Code == StoreErrorCodes.CustomEventIdentityConflict)
                {
                    Publish(new ReviewUpdate.SynchronizationWarning(
                        ApplicationErrors.CustomEventDuplicate));
                    return Result<CustomEventPublishOutcome>.Success(
                        CustomEventPublishOutcome.Duplicate);
                }

                return Result<CustomEventPublishOutcome>.Failure(recorded.Error!);
            }

            Publish(new ReviewUpdate.CustomEventChanged(stored.Session, stored.Id));
            return Result<CustomEventPublishOutcome>.Success(CustomEventPublishOutcome.Accepted);
        }
        finally
        {
            _receivedEventGate.Release();
        }
    }

    public Task<Result<UserPreferences>> GetPreferencesAsync(CancellationToken cancellationToken) =>
        _store.QueryAsync(GetPreferences.Instance, cancellationToken);

    public async Task<Result> UpdatePreferencesAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
        if (preferences.EventJoinCode is not null &&
            !JoinCode.TryParse(preferences.EventJoinCode).IsSuccess)
        {
            return Result.Failure(ApplicationErrors.InvalidEventJoinCode);
        }

        if (!preferences.AutoPause)
        {
            var playback = ReplayPlayback.TryCreatePlaying(preferences.PlaybackSpeed).Value;
            var validation = _replayController.ValidatePlayback(playback);
            if (!validation.IsSuccess)
            {
                return validation;
            }
        }

        var result = await ExecuteWithReconciliationAsync(
            UpdatePreferences.Create(preferences, Now()),
            cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            Publish(new ReviewUpdate.PreferencesChanged());
            lock (_stateLock)
            {
                if (_status == ReviewServiceStatus.Connected &&
                    _outboxFlushSafetyConfirmed)
                {
                    RequestOutboxFlush();
                }
            }
        }

        return result;
    }

    private async Task FlushPendingCustomEventsAsync(CancellationToken cancellationToken)
    {
        var preferences = await _store.QueryAsync(GetPreferences.Instance, cancellationToken)
            .ConfigureAwait(false);
        if (!preferences.IsSuccess || preferences.Value.EventJoinCode is null)
        {
            return;
        }

        var joinCode = JoinCode.TryParse(preferences.Value.EventJoinCode);
        if (!joinCode.IsSuccess)
        {
            Publish(new ReviewUpdate.SynchronizationWarning(
                ApplicationErrors.InvalidEventJoinCode));
            return;
        }

        var pending = await _store.QueryAsync(
            new GetPendingCustomEvents(joinCode.Value.SessionIdentity),
            cancellationToken).ConfigureAwait(false);
        if (!pending.IsSuccess)
        {
            Publish(new ReviewUpdate.SynchronizationWarning(pending.Error!));
            return;
        }

        foreach (var customEvent in pending.Value)
        {
            var submission = CustomEventSubmission.TryCreate(
                customEvent.Id,
                customEvent.Session,
                customEvent.Position,
                customEvent.Submitter,
                customEvent.OccurredAt);
            if (!submission.IsSuccess)
            {
                Publish(new ReviewUpdate.SynchronizationWarning(submission.Error!));
                continue;
            }

            var published = await _customEventPublisher.PublishAsync(
                joinCode.Value,
                submission.Value,
                cancellationToken).ConfigureAwait(false);
            if (!published.IsSuccess)
            {
                Publish(new ReviewUpdate.SynchronizationWarning(published.Error!));
                break;
            }

            var confirmedAt = Now();
            if (confirmedAt.UnixMilliseconds < customEvent.OccurredAt.UnixMilliseconds)
            {
                confirmedAt = customEvent.OccurredAt;
            }

            var marked = await ExecuteWithReconciliationAsync(
                MarkCustomEventSynchronized.Create(customEvent.Id, confirmedAt),
                cancellationToken).ConfigureAwait(false);
            if (!marked.IsSuccess)
            {
                Publish(new ReviewUpdate.SynchronizationWarning(marked.Error!));
                break;
            }

            if (published.Value == CustomEventPublishOutcome.Duplicate)
            {
                Publish(new ReviewUpdate.SynchronizationWarning(
                    ApplicationErrors.CustomEventDuplicate));
            }

            Publish(new ReviewUpdate.CustomEventChanged(customEvent.Session, customEvent.Id));
        }
    }

    public IAsyncEnumerable<ReviewUpdate> ObserveUpdatesAsync(CancellationToken cancellationToken) =>
        _updates.Reader.ReadAllAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var telemetryEvent in _telemetrySource
                               .ObserveAsync(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await HandleTelemetryEventAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }

            lock (_stateLock)
            {
                if (!_stopRequested)
                {
                    throw new InvalidOperationException("The telemetry stream completed unexpectedly.");
                }
            }

            CompleteExpectedStop();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteExpectedStop();
        }
        catch (Exception exception)
        {
            Action? cancelWorker;
            lock (_stateLock)
            {
                _terminalException = exception;
                cancelWorker = _cancelWorker;
            }

            cancelWorker?.Invoke();
            _updates.Writer.TryComplete(exception);
            _completion.TrySetException(exception);
        }
    }

    private async Task RunWithLifetimeAsync(CancellationTokenSource lifetime)
    {
        var outboxWorker = RunOutboxWorkerAsync(lifetime.Token);
        try
        {
            await RunAsync(lifetime.Token).ConfigureAwait(false);
            await outboxWorker.ConfigureAwait(false);
        }
        finally
        {
            lifetime.Dispose();
            lock (_stateLock)
            {
                _cancelWorker = null;
            }
        }
    }

    private async Task HandleTelemetryEventAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken)
    {
        switch (telemetryEvent)
        {
            case TelemetryConnected:
                lock (_stateLock)
                {
                    _activeOutboxFlushCancellation?.Cancel();
                    _outboxFlushSafetyConfirmed = false;
                    _outboxFlushAttemptedForOffTrackPeriod = false;
                }

                SetStatus(ReviewServiceStatus.Connected);
                break;
            case TelemetryDisconnected:
                lock (_stateLock)
                {
                    _activeOutboxFlushCancellation?.Cancel();
                    _activeSession = null;
                    _checkpoints.Clear();
                    _currentSession = null;
                    _onTrackState = OnTrackState.Unknown;
                    _replayDescriptor = null;
                    _ownedReviewActive = false;
                    _latestLiveSample = null;
                    _outboxFlushSafetyConfirmed = false;
                    _outboxFlushAttemptedForOffTrackPeriod = false;
                }

                SetStatus(ReviewServiceStatus.WaitingForSimulator);
                break;
            case TelemetryUnavailable unavailable:
                lock (_stateLock)
                {
                    _activeOutboxFlushCancellation?.Cancel();
                    _outboxFlushSafetyConfirmed = false;
                    _outboxFlushAttemptedForOffTrackPeriod = false;
                }

                SetUnavailable(unavailable.Error);
                break;
            case TelemetrySampleObserved observed:
                SessionIdentity? invalidatedSession = null;
                var shouldFlushOutbox = false;
                lock (_stateLock)
                {
                    if (!SampleMatchesCurrentSession(observed.Sample.Session))
                    {
                        invalidatedSession = _currentSession;
                        _currentSession = null;
                        _replayDescriptor = null;
                    }

                    _onTrackState = observed.Sample.OnTrackState;
                    _outboxFlushSafetyConfirmed =
                        _onTrackState == OnTrackState.NotOnTrack;
                    if (_onTrackState != OnTrackState.NotOnTrack)
                    {
                        _activeOutboxFlushCancellation?.Cancel();
                        _outboxFlushAttemptedForOffTrackPeriod = false;
                    }
                    else if (!_outboxFlushAttemptedForOffTrackPeriod)
                    {
                        shouldFlushOutbox = true;
                    }

                    if (_onTrackState == OnTrackState.OnTrack)
                    {
                        _ownedReviewActive = false;
                    }

                    if (observed.Sample.Session.Mode == SessionMode.Live)
                    {
                        _latestLiveSample = observed.Sample;
                    }
                }

                if (invalidatedSession is not null)
                {
                    PublishClearedSession(invalidatedSession);
                }

                if (await ProcessSampleAsync(observed.Sample, cancellationToken).ConfigureAwait(false))
                {
                    SetStatus(ReviewServiceStatus.Connected);
                    if (shouldFlushOutbox)
                    {
                        var requestFlush = false;
                        lock (_stateLock)
                        {
                            if (_outboxFlushSafetyConfirmed &&
                                !_outboxFlushAttemptedForOffTrackPeriod)
                            {
                                _outboxFlushAttemptedForOffTrackPeriod = true;
                                requestFlush = true;
                            }
                        }

                        if (requestFlush)
                        {
                            RequestOutboxFlush();
                        }
                    }
                }
                break;
            default:
                throw new InvalidOperationException("The telemetry event type is unsupported.");
        }
    }

    public void Dispose() => _receivedEventGate.Dispose();

    private void RequestOutboxFlush() => _outboxFlushRequests.Writer.TryWrite(0);

    private async Task ObserveCustomEventHostAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            Publish(new ReviewUpdate.SynchronizationWarning(
                ApplicationErrors.EventSyncUnavailable));
        }
    }

    private async Task RunOutboxWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _outboxFlushRequests.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                CancellationTokenSource? flushCancellation = null;
                lock (_stateLock)
                {
                    if (_status == ReviewServiceStatus.Connected &&
                        _outboxFlushSafetyConfirmed)
                    {
                        flushCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                        _activeOutboxFlushCancellation = flushCancellation;
                    }
                }

                if (flushCancellation is null)
                {
                    continue;
                }

                try
                {
                    await FlushPendingCustomEventsAsync(flushCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (flushCancellation.IsCancellationRequested)
                {
                }
                finally
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_activeOutboxFlushCancellation, flushCancellation))
                        {
                            _activeOutboxFlushCancellation = null;
                        }
                    }

                    flushCancellation.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Action? cancelWorker;
            lock (_stateLock)
            {
                _terminalException = exception;
                cancelWorker = _cancelWorker;
            }

            cancelWorker?.Invoke();
            _updates.Writer.TryComplete(exception);
            _completion.TrySetException(exception);
        }
    }

    private async Task<bool> ProcessSampleAsync(
        TelemetrySample sample,
        CancellationToken cancellationToken)
    {
        bool suppressLiveProcessing;
        lock (_stateLock)
        {
            suppressLiveProcessing = _ownedReviewActive &&
                sample.OnTrackState != OnTrackState.OnTrack &&
                sample.Session.Mode == SessionMode.Live;
        }

        if (suppressLiveProcessing)
        {
            return false;
        }

        if (!await CommitPendingTransitionAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (sample.Session.Mode == SessionMode.Replay)
        {
            return await ResolveReplaySessionAsync(sample, cancellationToken).ConfigureAwait(false);
        }

        var session = await ResolveLiveSessionAsync(sample, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return false;
        }

        foreach (var participantCounter in sample.IncidentCounters.OrderBy(
                     static counter => counter.Participant.Identity.Value,
                     StringComparer.Ordinal))
        {
            var observation = IncidentObservation.TryCreate(
                session.Id,
                participantCounter.Participant,
                sample.Position,
                participantCounter.IncidentCounter,
                participantCounter.Lap,
                participantCounter.LapDistance,
                sample.ObservedAt);
            if (!observation.IsSuccess)
            {
                SetUnavailable(observation.Error!);
                return false;
            }

            IncidentCheckpoint? checkpoint;
            lock (_stateLock)
            {
                _ = _checkpoints.TryGetValue(
                    participantCounter.Participant.Identity,
                    out checkpoint);
            }

            var decision = IncidentCounterTransition.Evaluate(checkpoint, observation.Value);
            if (!decision.IsSuccess)
            {
                SetUnavailable(decision.Error!);
                return false;
            }

            if (decision.Value is IncidentTransitionDecision.NoChange)
            {
                continue;
            }

            var pendingResult = decision.Value switch
            {
                IncidentTransitionDecision.EstablishBaseline baseline =>
                    CreatePendingBaseline(checkpoint, baseline),
                IncidentTransitionDecision.RecordIncrease increase =>
                    CreatePendingIncrease(session.Id, increase),
                _ => throw new InvalidOperationException(
                    "The incident transition type is unsupported."),
            };

            if (!pendingResult.IsSuccess)
            {
                SetUnavailable(pendingResult.Error!);
                return false;
            }

            lock (_stateLock)
            {
                _pendingTransition = pendingResult.Value;
            }

            if (!await CommitPendingTransitionAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private static Result<PendingStoreTransition> CreatePendingBaseline(
        IncidentCheckpoint? checkpoint,
        IncidentTransitionDecision.EstablishBaseline baseline)
    {
        var command = EstablishIncidentCheckpoint.TryCreate(checkpoint, baseline.NextCheckpoint);
        if (!command.IsSuccess)
        {
            return Result<PendingStoreTransition>.Failure(command.Error!);
        }

        return Result<PendingStoreTransition>.Success(new PendingStoreTransition(
            command.Value,
            baseline.NextCheckpoint,
            Incident: null));
    }

    private static Result<PendingStoreTransition> CreatePendingIncrease(
        SessionIdentity session,
        IncidentTransitionDecision.RecordIncrease increase)
    {
        var emptyAnnotation = IncidentAnnotation.TryCreate(null, null).Value;
        var incident = StoredIncident.Create(
            IncidentId.CreateDetected(
                session,
                increase.Observation.Participant.Identity,
                increase.NextCheckpoint.CounterEpoch,
                increase.Points),
            session,
            increase.Observation.Participant,
            increase.Observation.Position,
            increase.Observation.ObservedAt,
            increase.Points,
            increase.NextCheckpoint.CounterEpoch,
            increase.Observation.Lap,
            increase.Observation.LapDistance,
            IncidentReviewStatus.Pending,
            emptyAnnotation,
            increase.Observation.ObservedAt,
            increase.Observation.ObservedAt);
        var command = RecordDetectedIncident.TryCreate(
            incident,
            increase.ExpectedCheckpoint,
            increase.NextCheckpoint);
        if (!command.IsSuccess)
        {
            return Result<PendingStoreTransition>.Failure(command.Error!);
        }

        return Result<PendingStoreTransition>.Success(new PendingStoreTransition(
            command.Value,
            increase.NextCheckpoint,
            incident));
    }

    private async Task<StoredSession?> ResolveLiveSessionAsync(
        TelemetrySample sample,
        CancellationToken cancellationToken)
    {
        StoredSession? cachedSession;
        var restoredCurrentSession = false;
        lock (_stateLock)
        {
            _replayDescriptor = null;
            cachedSession = _activeSession is { } activeSession &&
                IsSameSimulatorEvent(activeSession.Descriptor, sample.Session)
                    ? activeSession
                    : null;
            if (cachedSession is not null && _currentSession != cachedSession.Id)
            {
                _currentSession = cachedSession.Id;
                restoredCurrentSession = true;
            }
            else if (cachedSession is null)
            {
                _currentSession = null;
            }
        }

        if (cachedSession is not null)
        {
            if (restoredCurrentSession)
            {
                Publish(new ReviewUpdate.SessionChanged(cachedSession.Id));
            }

            return cachedSession;
        }

        StoredSession? resolved = null;
        if (sample.Session.IdentityScope == SimulatorIdentityScope.Durable)
        {
            var lookup = await _store.QueryAsync(
                new GetSessionBySimulatorKey(sample.Session.Simulator, sample.Session.SessionKey),
                cancellationToken).ConfigureAwait(false);
            if (!lookup.IsSuccess)
            {
                SetUnavailable(lookup.Error!);
                return null;
            }

            resolved = lookup.Value.Value;
        }

        if (resolved is not null &&
            sample.Session.IdentityScope == SimulatorIdentityScope.Durable)
        {
            var deterministicIdentity = SessionIdentity.CreateDurable(
                sample.Session.Simulator,
                sample.Session.SessionKey);
            if (resolved.Id != deterministicIdentity)
            {
                var promotion = PromoteSessionIdentity.TryCreate(
                    resolved.Id,
                    resolved.Descriptor,
                    sample.ObservedAt);
                if (!promotion.IsSuccess)
                {
                    SetUnavailable(StoreErrors.SessionIdentityConflict);
                    return null;
                }

                var promoted = await ExecuteWithReconciliationAsync(
                    promotion.Value,
                    cancellationToken).ConfigureAwait(false);
                if (promoted.IsSuccess)
                {
                    resolved = StoredSession.Create(
                        promotion.Value.DeterministicIdentity,
                        resolved.Descriptor,
                        resolved.StartedAt);
                }
                else if (promoted.Error!.Code == StoreErrorCodes.SessionIdentityConflict ||
                    promoted.Error.Code == StoreErrorCodes.EntityNotFound)
                {
                    var winner = await _store.QueryAsync(
                        new GetSessionBySimulatorKey(
                            sample.Session.Simulator,
                            sample.Session.SessionKey),
                        cancellationToken).ConfigureAwait(false);
                    if (!winner.IsSuccess ||
                        !winner.Value.IsFound ||
                        winner.Value.Value!.Id != deterministicIdentity)
                    {
                        SetUnavailable(winner.IsSuccess ? promoted.Error : winner.Error!);
                        return null;
                    }

                    resolved = winner.Value.Value;
                }
                else
                {
                    SetUnavailable(promoted.Error);
                    return null;
                }
            }
        }

        if (resolved is null)
        {
            var proposedIdentity = sample.Session.IdentityScope == SimulatorIdentityScope.Durable
                ? SessionIdentity.CreateDurable(
                    sample.Session.Simulator,
                    sample.Session.SessionKey)
                : SessionIdentity.Generate();
            var ensure = EnsureSession.Create(proposedIdentity, sample.Session, sample.ObservedAt);
            var result = await ExecuteWithReconciliationAsync(ensure, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error!.Code != StoreErrorCodes.SessionIdentityConflict)
            {
                SetUnavailable(result.Error);
                return null;
            }

            if (result.IsSuccess)
            {
                resolved = StoredSession.Create(
                    ensure.ProposedIdentity,
                    ensure.Descriptor,
                    ensure.StartedAt);
            }
            else if (sample.Session.IdentityScope == SimulatorIdentityScope.Durable)
            {
                var winner = await _store.QueryAsync(
                    new GetSessionBySimulatorKey(sample.Session.Simulator, sample.Session.SessionKey),
                    cancellationToken).ConfigureAwait(false);
                if (!winner.IsSuccess || !winner.Value.IsFound)
                {
                    SetUnavailable(winner.IsSuccess ? result.Error! : winner.Error!);
                    return null;
                }

                resolved = winner.Value.Value;
            }
        }

        if (resolved is null || !IsSameSimulatorEvent(resolved.Descriptor, sample.Session))
        {
            SetUnavailable(StoreErrors.SessionIdentityConflict);
            return null;
        }

        var checkpoints = await _store.QueryAsync(
            new GetIncidentCheckpoints(resolved.Id),
            cancellationToken).ConfigureAwait(false);
        if (!checkpoints.IsSuccess)
        {
            SetUnavailable(checkpoints.Error!);
            return null;
        }

        if (checkpoints.Value.Any(checkpoint => checkpoint.Session != resolved.Id) ||
            checkpoints.Value.Select(static checkpoint => checkpoint.ParticipantIdentity)
                .Distinct()
                .Count() != checkpoints.Value.Count)
        {
            SetUnavailable(StoreErrors.PersistenceFailure);
            return null;
        }

        lock (_stateLock)
        {
            _activeSession = resolved;
            _currentSession = resolved.Id;
            _checkpoints.Clear();
            foreach (var checkpoint in checkpoints.Value)
            {
                _checkpoints.Add(checkpoint.ParticipantIdentity, checkpoint);
            }
        }

        Publish(new ReviewUpdate.SessionChanged(resolved.Id));
        return resolved;
    }

    private async Task<bool> ResolveReplaySessionAsync(
        TelemetrySample sample,
        CancellationToken cancellationToken)
    {
        SessionIdentity? previousSession;
        SessionIdentity? connectionScopedSession = null;
        lock (_stateLock)
        {
            if (_replayDescriptor is not null &&
                IsSameSimulatorEvent(_replayDescriptor, sample.Session))
            {
                _replayDescriptor = sample.Session;
                return true;
            }

            previousSession = _currentSession;
            if (_activeSession is { } activeSession &&
                IsSameSimulatorEvent(activeSession.Descriptor, sample.Session))
            {
                _replayDescriptor = sample.Session;
                _currentSession = activeSession.Id;
                return true;
            }

            if (sample.Session.IdentityScope == SimulatorIdentityScope.ConnectionScoped)
            {
                _replayDescriptor = sample.Session;
                connectionScopedSession = _activeSession is not null &&
                    IsMatchingConnectionScopedReplay(_activeSession.Descriptor, sample.Session)
                        ? _activeSession.Id
                        : null;
                _currentSession = connectionScopedSession;
            }
            else
            {
                _activeSession = null;
                _checkpoints.Clear();
                _currentSession = null;
                _replayDescriptor = null;
            }
        }

        if (sample.Session.IdentityScope != SimulatorIdentityScope.Durable)
        {
            if (connectionScopedSession != previousSession)
            {
                if (connectionScopedSession is not null)
                {
                    Publish(new ReviewUpdate.SessionChanged(connectionScopedSession));
                }
                else
                {
                    PublishClearedSession(previousSession);
                }
            }

            return true;
        }

        var lookup = await _store.QueryAsync(
            new GetSessionBySimulatorKey(sample.Session.Simulator, sample.Session.SessionKey),
            cancellationToken).ConfigureAwait(false);
        if (!lookup.IsSuccess)
        {
            SetUnavailable(lookup.Error!);
            PublishClearedSession(previousSession);
            return false;
        }

        var resolvedSession = lookup.Value.Value?.Id;
        lock (_stateLock)
        {
            _replayDescriptor = sample.Session;
            _currentSession = resolvedSession;
        }

        if (resolvedSession != previousSession)
        {
            if (resolvedSession is not null)
            {
                Publish(new ReviewUpdate.SessionChanged(resolvedSession));
            }
            else
            {
                PublishClearedSession(previousSession);
            }
        }

        return true;
    }

    private static bool IsMatchingConnectionScopedReplay(
        SimulatorSessionDescriptor activeSession,
        SimulatorSessionDescriptor replaySession) =>
        activeSession.Mode == SessionMode.Live &&
        activeSession.IdentityScope == SimulatorIdentityScope.ConnectionScoped &&
        replaySession.Mode == SessionMode.Replay &&
        replaySession.IdentityScope == SimulatorIdentityScope.ConnectionScoped &&
        IsSameSimulatorEvent(activeSession, replaySession);

    private bool SampleMatchesCurrentSession(SimulatorSessionDescriptor sampleSession)
    {
        if (_currentSession is null)
        {
            return false;
        }

        var activeSession = _activeSession;
        return (activeSession is not null &&
                activeSession.Id == _currentSession &&
                IsSameSimulatorEvent(activeSession.Descriptor, sampleSession)) ||
            (_replayDescriptor is not null &&
                IsSameSimulatorEvent(_replayDescriptor, sampleSession));
    }

    private static bool IsSameSimulatorEvent(
        SimulatorSessionDescriptor first,
        SimulatorSessionDescriptor second) =>
        first.IdentityScope == second.IdentityScope &&
        first.Simulator == second.Simulator &&
        first.SessionKey == second.SessionKey;

    private void PublishClearedSession(SessionIdentity? previousSession)
    {
        if (previousSession is not null)
        {
            Publish(new ReviewUpdate.SessionChanged(previousSession));
        }
    }

    private async Task<bool> CommitPendingTransitionAsync(CancellationToken cancellationToken)
    {
        PendingStoreTransition? pending;
        lock (_stateLock)
        {
            pending = _pendingTransition;
        }

        if (pending is null)
        {
            return true;
        }

        var retryDelay = InitialPersistenceRetryDelay;
        while (true)
        {
            var result = await ExecuteWithReconciliationAsync(pending.Command, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                break;
            }

            SetUnavailable(result.Error!);
            if (!IsRetryableStoreFailure(result.Error!))
            {
                return false;
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            retryDelay = TimeSpan.FromTicks(Math.Min(
                retryDelay.Ticks * 2,
                MaximumPersistenceRetryDelay.Ticks));
        }

        lock (_stateLock)
        {
            _pendingTransition = null;
            if (_activeSession?.Id == pending.NextCheckpoint.Session)
            {
                _checkpoints[pending.NextCheckpoint.ParticipantIdentity] =
                    pending.NextCheckpoint;
            }
        }

        if (pending.Incident is not null)
        {
            Publish(new ReviewUpdate.IncidentChanged(
                pending.Incident.Session,
                pending.Incident.Id));
        }

        return true;
    }

    private static bool IsRetryableStoreFailure(Error error) => error.Kind is
        ErrorKind.Unavailable or ErrorKind.Persistence or ErrorKind.Indeterminate;

    private async Task<Result> ExecuteWithReconciliationAsync(
        IStoreCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _store.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        if (!IsIndeterminate(result))
        {
            return result;
        }

        try
        {
            var outcome = await _store.QueryAsync(
                new GetOperationOutcome(command.OperationId),
                cancellationToken).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                return result;
            }

            if (outcome.Value.IsCommitted)
            {
                return Result.Success();
            }

            result = await _store.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (!IsIndeterminate(result))
            {
                return result;
            }

            outcome = await _store.QueryAsync(
                new GetOperationOutcome(command.OperationId),
                cancellationToken).ConfigureAwait(false);
            return outcome.IsSuccess && outcome.Value.IsCommitted
                ? Result.Success()
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return result;
        }
    }

    private static bool IsIndeterminate(Result result) =>
        !result.IsSuccess && result.Error!.Code == StoreErrorCodes.IndeterminateCommit;

    private static ReviewSession MapSession(StoredSessionDetails details) => ReviewSession.Create(
        details.Session.Id,
        details.Session.Descriptor,
        details.Session.StartedAt,
        details.Incidents.Select(MapIncident),
        details.CustomEvents.Select(MapCustomEvent));

    private static ReviewIncident MapIncident(StoredIncident incident) => ReviewIncident.Create(
        incident.Id,
        incident.Session,
        incident.Participant,
        incident.Position,
        incident.ObservedAt,
        incident.Points,
        incident.CounterEpoch,
        incident.Lap,
        incident.LapDistance,
        incident.ReviewStatus,
        incident.Annotation);

    private static ReviewCustomEvent MapCustomEvent(StoredCustomEvent customEvent) =>
        ReviewCustomEvent.Create(
            customEvent.Id,
            customEvent.Session,
            customEvent.Position,
            customEvent.Submitter,
            customEvent.OccurredAt,
            customEvent.Synchronization is CustomEventSynchronization.Synchronized);

    private UtcInstant Now() => UtcInstant.TryCreateUnixMilliseconds(
        _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()).Value;

    private void SetStatus(ReviewServiceStatus status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_status != status)
            {
                _status = status;
                changed = true;
            }

            if (status != ReviewServiceStatus.Unavailable)
            {
                _lastUnavailableError = null;
            }
        }

        if (changed)
        {
            Publish(ReviewUpdate.StatusChanged.Create(status));
        }
    }

    private void SetUnavailable(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var changed = false;
        lock (_stateLock)
        {
            if (_status != ReviewServiceStatus.Unavailable ||
                _lastUnavailableError != error)
            {
                _status = ReviewServiceStatus.Unavailable;
                _lastUnavailableError = error;
                changed = true;
            }
        }

        if (changed)
        {
            Publish(ReviewUpdate.StatusChanged.Create(ReviewServiceStatus.Unavailable, error));
        }
    }

    private void Publish(ReviewUpdate update)
    {
        lock (_stateLock)
        {
            _snapshotRevision = checked(_snapshotRevision + 1);
            if (!_updates.Writer.TryWrite(update) && !_stopRequested)
            {
                throw new InvalidOperationException("The review-update stream is closed.");
            }
        }
    }

    private SnapshotAnchor CaptureSnapshotAnchor()
    {
        lock (_stateLock)
        {
            return new SnapshotAnchor(
                _snapshotRevision,
                _status,
                _lastUnavailableError,
                _currentSession);
        }
    }

    private bool IsSnapshotAnchorCurrent(SnapshotAnchor anchor)
    {
        lock (_stateLock)
        {
            return anchor == new SnapshotAnchor(
                _snapshotRevision,
                _status,
                _lastUnavailableError,
                _currentSession);
        }
    }

    private void CompleteExpectedStop()
    {
        lock (_stateLock)
        {
            if (_terminalException is not null)
            {
                return;
            }

            _status = ReviewServiceStatus.Stopped;
            _lastUnavailableError = null;
            _ownedReviewActive = false;
            _replayDescriptor = null;
            _snapshotRevision = checked(_snapshotRevision + 1);
            _ = _updates.Writer.TryWrite(
                ReviewUpdate.StatusChanged.Create(ReviewServiceStatus.Stopped));
        }

        _updates.Writer.TryComplete();
        _completion.TrySetResult();
    }

    private sealed record PendingStoreTransition(
        IStoreCommand Command,
        IncidentCheckpoint NextCheckpoint,
        StoredIncident? Incident);

    private sealed record SnapshotAnchor(
        long Revision,
        ReviewServiceStatus Status,
        Error? StatusError,
        SessionIdentity? CurrentSession);
}
