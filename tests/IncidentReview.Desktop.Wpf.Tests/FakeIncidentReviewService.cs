using System.Threading.Channels;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

internal sealed class FakeIncidentReviewService : IIncidentReviewService
{
    private readonly Channel<ReviewUpdate> _updates = Channel.CreateUnbounded<ReviewUpdate>();

    public Result<ReviewSnapshot> SnapshotResult { get; set; } =
        Result<ReviewSnapshot>.Success(TestModelFactory.Snapshot());

    public Result<ReviewServiceStatus> StatusResult { get; set; } =
        Result<ReviewServiceStatus>.Success(ReviewServiceStatus.Connected);

    public Result<ReviewSession> CurrentSessionResult { get; set; } =
        Result<ReviewSession>.Failure(ApplicationErrors.NoCurrentSession);

    public Result<IReadOnlyList<SessionSummary>> SessionsResult { get; set; } =
        Result<IReadOnlyList<SessionSummary>>.Success(Array.Empty<SessionSummary>());

    public Result<UserPreferences> PreferencesResult { get; set; } =
        Result<UserPreferences>.Success(TestModelFactory.Preferences());

    public Result ReviewResult { get; set; } = Result.Success();

    public Result UpdatePreferencesResult { get; set; } = Result.Success();

    public Dictionary<SessionIdentity, ReviewSession> Sessions { get; } = [];

    public IncidentId? ReviewedIncident { get; private set; }

    public ReplayOffset? ReviewedOffset { get; private set; }

    public List<ReplayOffset> ReviewedOffsets { get; } = [];

    public int ReviewCalls { get; private set; }

    public UserPreferences? UpdatedPreferences { get; private set; }

    public int StatusCalls { get; private set; }

    public int SnapshotCalls { get; private set; }

    public Func<CancellationToken, Task<Result<ReviewServiceStatus>>>? StatusHandler { get; set; }

    public Func<CancellationToken, Task<Result<ReviewSnapshot>>>? SnapshotHandler { get; set; }

    public Func<IncidentId, ReplayOffset, CancellationToken, Task<Result>>? ReviewHandler { get; set; }

    public Task<Result<ReviewServiceStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        StatusCalls++;
        return StatusHandler is null
            ? Task.FromResult(StatusResult)
            : StatusHandler(cancellationToken);
    }

    public Task<Result<ReviewSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        SnapshotCalls++;
        return SnapshotHandler is null
            ? Task.FromResult(SnapshotResult)
            : SnapshotHandler(cancellationToken);
    }

    public Task<Result<ReviewSession>> GetCurrentSessionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentSessionResult);

    public Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken) => Task.FromResult(SessionsResult);

    public Task<Result<ReviewSession>> GetSessionAsync(
        SessionIdentity session,
        CancellationToken cancellationToken) => Task.FromResult(
            Sessions.TryGetValue(session, out var value)
                ? Result<ReviewSession>.Success(value)
                : Result<ReviewSession>.Failure(ApplicationErrors.SessionNotFound));

    public Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        ReplayOffset offset,
        CancellationToken cancellationToken)
    {
        ReviewedIncident = incidentId;
        ReviewedOffset = offset;
        ReviewedOffsets.Add(offset);
        ReviewCalls++;
        return ReviewHandler is null
            ? Task.FromResult(ReviewResult)
            : ReviewHandler(incidentId, offset, cancellationToken);
    }

    public Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        CancellationToken cancellationToken) =>
        ReviewIncidentAsync(incidentId, ReplayOffset.Zero, cancellationToken);

    public Task<Result> AnnotateIncidentAsync(
        IncidentId incidentId,
        IncidentAnnotation annotation,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<UserPreferences>> GetPreferencesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PreferencesResult);

    public Task<Result> UpdatePreferencesAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken)
    {
        UpdatedPreferences = preferences;
        if (UpdatePreferencesResult.IsSuccess && SnapshotResult.IsSuccess)
        {
            var current = SnapshotResult.Value;
            SnapshotResult = Result<ReviewSnapshot>.Success(ReviewSnapshot.Create(
                current.Revision + 1,
                current.Status,
                current.StatusError,
                current.ActiveSession,
                preferences,
                current.DriverDisplayName,
                current.CameraGroups,
                current.CurrentCameraGroup));
        }

        return Task.FromResult(UpdatePreferencesResult);
    }

    public IAsyncEnumerable<ReviewUpdate> ObserveUpdatesAsync(CancellationToken cancellationToken) =>
        _updates.Reader.ReadAllAsync(cancellationToken);

    public ValueTask PublishAsync(ReviewUpdate update) => _updates.Writer.WriteAsync(update);
}

internal sealed class RecordingDispatcher : IUiDispatcher
{
    public int InvocationCount { get; private set; }

    public bool CheckAccess() => false;

    public Task InvokeAsync(Action action)
    {
        InvocationCount++;
        action();
        return Task.CompletedTask;
    }
}
