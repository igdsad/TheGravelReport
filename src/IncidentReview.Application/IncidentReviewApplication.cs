using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;
using IncidentReview.Store.Contracts;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Application;

internal sealed class IncidentReviewApplication : IIncidentReviewService, IApplicationRuntime
{
    private static readonly TimeSpan InitialPersistenceRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumPersistenceRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly ReplayContext EmptyReplayContext =
        ReplayContext.TryCreate(null, [], null).Value;

    private readonly ITelemetrySource _telemetrySource;
    private readonly IReplayController _replayController;
    private readonly IReplayContextReader _replayContextReader;
    private readonly IStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateLock = new();
    private readonly Channel<ReviewUpdate> _updates = Channel.CreateUnbounded<ReviewUpdate>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly TaskCompletionSource _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? _cancelWorker;
    private Task? _worker;
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

    public IncidentReviewApplication(
        ITelemetrySource telemetrySource,
        IReplayController replayController,
        IReplayContextReader replayContextReader,
        IStore store,
        TimeProvider timeProvider)
    {
        _telemetrySource = telemetrySource;
        _replayController = replayController;
        _replayContextReader = replayContextReader;
        _store = store;
        _timeProvider = timeProvider;
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
        var reviewArmed = false;
        var seekAccepted = false;
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

            var preferences = await _store.QueryAsync(GetPreferences.Instance, cancellationToken)
                .ConfigureAwait(false);
            if (!preferences.IsSuccess)
            {
                return Result.Failure(preferences.Error!);
            }

            var stored = incident.Value.Value!;
            var effectiveOffset = offset ?? ReplayOffset.TryCreateMilliseconds(
                -preferences.Value.ReplayLeadInMilliseconds).Value;
            var targetTime = effectiveOffset.ApplyTo(stored.Position.SessionTime);
            if (!targetTime.IsSuccess)
            {
                return Result.Failure(targetTime.Error!);
            }

            var target = ReplayPosition.TryCreate(
                stored.Position.SessionNumber,
                targetTime.Value).Value;
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
                    stored.Session,
                    stored.Position.SessionNumber);
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
            var focus = await _replayController.FocusParticipantAsync(
                    stored.Participant,
                    preferences.Value.PreferredCamera,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!focus.IsSuccess)
            {
                return focus;
            }

            return await _replayController.SetPlaybackAsync(playback, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                _reviewCommandInProgress = false;
                if (reviewArmed && !seekAccepted)
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

    public Task<Result<UserPreferences>> GetPreferencesAsync(CancellationToken cancellationToken) =>
        _store.QueryAsync(GetPreferences.Instance, cancellationToken);

    public async Task<Result> UpdatePreferencesAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
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
        }

        return result;
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
            lock (_stateLock)
            {
                _terminalException = exception;
            }

            _updates.Writer.TryComplete(exception);
            _completion.TrySetException(exception);
        }
    }

    private async Task RunWithLifetimeAsync(CancellationTokenSource lifetime)
    {
        try
        {
            await RunAsync(lifetime.Token).ConfigureAwait(false);
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
                SetStatus(ReviewServiceStatus.Connected);
                break;
            case TelemetryDisconnected:
                lock (_stateLock)
                {
                    _activeSession = null;
                    _checkpoints.Clear();
                    _currentSession = null;
                    _onTrackState = OnTrackState.Unknown;
                    _replayDescriptor = null;
                    _ownedReviewActive = false;
                }

                SetStatus(ReviewServiceStatus.WaitingForSimulator);
                break;
            case TelemetryUnavailable unavailable:
                SetUnavailable(unavailable.Error);
                break;
            case TelemetrySampleObserved observed:
                SessionIdentity? invalidatedSession = null;
                lock (_stateLock)
                {
                    if (!SampleMatchesCurrentSession(observed.Sample.Session))
                    {
                        invalidatedSession = _currentSession;
                        _currentSession = null;
                        _replayDescriptor = null;
                    }

                    _onTrackState = observed.Sample.OnTrackState;
                    if (_onTrackState == OnTrackState.OnTrack)
                    {
                        _ownedReviewActive = false;
                    }
                }

                if (invalidatedSession is not null)
                {
                    PublishClearedSession(invalidatedSession);
                }

                if (await ProcessSampleAsync(observed.Sample, cancellationToken).ConfigureAwait(false))
                {
                    SetStatus(ReviewServiceStatus.Connected);
                }
                break;
            default:
                throw new InvalidOperationException("The telemetry event type is unsupported.");
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
            IncidentId.Generate(),
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
            cachedSession = _activeSession?.Descriptor == sample.Session
                ? _activeSession
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

        if (resolved is null)
        {
            var ensure = EnsureSession.Create(SessionIdentity.Generate(), sample.Session, sample.ObservedAt);
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

        if (resolved is null || resolved.Descriptor.SessionNumber != sample.Session.SessionNumber)
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
            if (_replayDescriptor == sample.Session)
            {
                return true;
            }

            previousSession = _currentSession;
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
        IsSameSimulatorSession(activeSession, replaySession);

    private bool SampleMatchesCurrentSession(SimulatorSessionDescriptor sampleSession)
    {
        if (_currentSession is null)
        {
            return false;
        }

        var activeSession = _activeSession;
        return (activeSession is not null &&
                activeSession.Id == _currentSession &&
                IsSameSimulatorSession(activeSession.Descriptor, sampleSession)) ||
            (_replayDescriptor is not null &&
                IsSameSimulatorSession(_replayDescriptor, sampleSession));
    }

    private static bool IsSameSimulatorSession(
        SimulatorSessionDescriptor first,
        SimulatorSessionDescriptor second) =>
        first.IdentityScope == second.IdentityScope &&
        first.Simulator == second.Simulator &&
        first.SessionKey == second.SessionKey &&
        first.SessionNumber == second.SessionNumber;

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
        details.Incidents.Select(MapIncident));

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
