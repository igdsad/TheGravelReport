using IncidentReview.Application.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task RefreshDisplaysCurrentSessionIncidentsChronologically()
    {
        var service = new FakeIncidentReviewService();
        var sessionId = IncidentReview.Domain.SessionIdentity.Generate();
        var later = TestModelFactory.Incident(sessionId, 2_000, 15_000, 4, 2);
        var earlier = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var session = TestModelFactory.Session(sessionId, later, earlier);
        service.CurrentSessionResult = Result<ReviewSession>.Success(session);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(session)]);
        var dispatcher = new RecordingDispatcher();
        await using var viewModel = new MainWindowViewModel(service, dispatcher);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Connected to iRacing", viewModel.ConnectionStatus);
        Assert.HasCount(1, viewModel.Sessions);
        Assert.IsTrue(viewModel.Sessions[0].IsCurrent);
        StringAssert.StartsWith(viewModel.Sessions[0].DisplayName, "Current");
        Assert.HasCount(2, viewModel.Incidents);
        Assert.AreEqual(earlier.Id, viewModel.Incidents[0].Id);
        Assert.AreEqual(later.Id, viewModel.Incidents[1].Id);
        Assert.AreEqual("3000", viewModel.ReplayLeadInMilliseconds);
        Assert.IsGreaterThan(0, dispatcher.InvocationCount);
        Assert.IsFalse(viewModel.ShowsEmptyState);
        Assert.AreEqual("2 incidents", viewModel.StatusDetail);
        Assert.HasCount(1, viewModel.EventLog);
        Assert.IsFalse(viewModel.EventLog[0].IsError);
        StringAssert.Contains(viewModel.EventLog[0].Message, "2 incidents");
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task CurrentSessionIsShownWhenSessionListSnapshotPrecedesIt()
    {
        var service = new FakeIncidentReviewService();
        var session = TestModelFactory.Session();
        service.CurrentSessionResult = Result<ReviewSession>.Success(session);
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.HasCount(1, viewModel.Sessions);
        Assert.AreEqual(session.Id, viewModel.Sessions[0].Id);
        Assert.IsTrue(viewModel.Sessions[0].IsCurrent);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task RefreshWithNoCurrentOrPastSessionShowsEmptyState()
    {
        var service = new FakeIncidentReviewService
        {
            StatusResult = Result<ReviewServiceStatus>.Success(
                ReviewServiceStatus.WaitingForSimulator),
        };
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Waiting for iRacing", viewModel.ConnectionStatus);
        Assert.IsTrue(viewModel.ShowsEmptyState);
        Assert.AreEqual("No review session is available yet.", viewModel.EmptyMessage);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task ReviewSelectedIncidentDisplaysExactActionableFailure()
    {
        var service = new FakeIncidentReviewService
        {
            ReviewResult = Result.Failure(ApplicationErrors.ReplayDriverOnTrack),
        };
        var sessionId = IncidentReview.Domain.SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var session = TestModelFactory.Session(sessionId, incident);
        service.CurrentSessionResult = Result<ReviewSession>.Success(session);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(session)]);
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedIncident = viewModel.Incidents[0];

        await viewModel.ReviewSelectedIncidentAsync(CancellationToken.None);

        Assert.AreEqual(incident.Id, service.ReviewedIncident);
        Assert.AreEqual(ApplicationErrors.ReplayDriverOnTrack.Message, viewModel.ErrorMessage);
        Assert.AreEqual(ApplicationErrors.ReplayDriverOnTrack.Message, viewModel.StatusDetail);
        Assert.IsTrue(viewModel.EventLog[^1].IsError);
        Assert.AreEqual(ApplicationErrors.ReplayDriverOnTrack.Message, viewModel.EventLog[^1].Message);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task EventLogDropsOldestEntriesAtItsBound()
    {
        var service = new FakeIncidentReviewService
        {
            ReviewResult = Result.Failure(ApplicationErrors.ReplayDriverOnTrack),
        };
        var sessionId = IncidentReview.Domain.SessionIdentity.Generate();
        var incident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var session = TestModelFactory.Session(sessionId, incident);
        service.CurrentSessionResult = Result<ReviewSession>.Success(session);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(session)]);
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedIncident = viewModel.Incidents[0];

        for (var index = 0; index < 205; index++)
        {
            await viewModel.ReviewSelectedIncidentAsync(CancellationToken.None);
        }

        Assert.HasCount(200, viewModel.EventLog);
        Assert.IsTrue(viewModel.EventLog.All(static entry => entry.IsError));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-HIS-001")]
    public async Task SelectingPastSessionLoadsItsIncidentSnapshot()
    {
        var service = new FakeIncidentReviewService();
        var current = TestModelFactory.Session();
        var pastId = IncidentReview.Domain.SessionIdentity.Generate();
        var pastIncident = TestModelFactory.Incident(pastId, 500, 4_000, 1, 1);
        var past = TestModelFactory.Session(pastId, pastIncident);
        service.CurrentSessionResult = Result<ReviewSession>.Success(current);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(current), TestModelFactory.Summary(past)]);
        service.Sessions.Add(past.Id, past);
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedSession = viewModel.Sessions.Single(item => item.Id == past.Id);

        await viewModel.OpenSelectedSessionAsync(CancellationToken.None);

        Assert.HasCount(1, viewModel.Incidents);
        Assert.AreEqual(pastIncident.Id, viewModel.Incidents[0].Id);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task SavePreferencesValidatesBeforeCallingApplication()
    {
        var service = new FakeIncidentReviewService();
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher())
        {
            ReplayLeadInMilliseconds = "not-a-number",
        };

        await viewModel.SavePreferencesAsync(CancellationToken.None);
        Assert.IsNull(service.UpdatedPreferences);
        Assert.IsTrue(viewModel.HasError);

        viewModel.ReplayLeadInMilliseconds = "5000";
        viewModel.PlaybackSpeed = "0.75";
        viewModel.AutoPause = true;
        viewModel.PreferredCamera = "TV1";
        await viewModel.SavePreferencesAsync(CancellationToken.None);

        Assert.IsNotNull(service.UpdatedPreferences);
        Assert.AreEqual(5_000, service.UpdatedPreferences.ReplayLeadInMilliseconds);
        Assert.AreEqual(0.75, service.UpdatedPreferences.PlaybackSpeed);
        Assert.AreEqual("TV1", service.UpdatedPreferences.PreferredCamera);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-002")]
    public async Task CancelActiveOperationReturnsViewModelToIdle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeIncidentReviewService
        {
            StatusHandler = async cancellationToken =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result<ReviewServiceStatus>.Success(ReviewServiceStatus.Connected);
            },
        };
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await entered.Task;
        Assert.IsTrue(viewModel.IsBusy);
        viewModel.CancelActiveOperation();
        await refresh;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    public async Task MonitorRefreshesAfterApplicationUpdate()
    {
        var service = new FakeIncidentReviewService();
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.StartMonitoringAsync(CancellationToken.None);
        var callsBeforeUpdate = service.StatusCalls;

        await service.PublishAsync(ReviewUpdate.StatusChanged.Create(
            ReviewServiceStatus.WaitingForSimulator));
        await WaitUntilAsync(() => service.StatusCalls > callsBeforeUpdate);

        Assert.IsTrue(viewModel.IsMonitoring);
        await viewModel.StopMonitoringAsync();
        Assert.IsFalse(viewModel.IsMonitoring);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task IncidentChangedAutomaticRefreshPreservesDirtyPreferencesAndSelectedIncident()
    {
        await AssertAutomaticRefreshPreservesInteractionStateAsync(
            static (session, incident) => new ReviewUpdate.IncidentChanged(session, incident));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "IR-SET-001")]
    public async Task SessionChangedAutomaticRefreshPreservesDirtyPreferencesAndSelectedIncident()
    {
        await AssertAutomaticRefreshPreservesInteractionStateAsync(
            static (session, _) => new ReviewUpdate.SessionChanged(session));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-002")]
    public async Task UnavailableUpdateKeepsItsActionableErrorThroughManualRefresh()
    {
        var service = new FakeIncidentReviewService
        {
            StatusResult = Result<ReviewServiceStatus>.Success(ReviewServiceStatus.Unavailable),
        };
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.StartMonitoringAsync(CancellationToken.None);
        var callsBeforeUpdate = service.StatusCalls;

        await service.PublishAsync(ReviewUpdate.StatusChanged.Create(
            ReviewServiceStatus.Unavailable,
            ApplicationErrors.ReplayUnavailable));
        await WaitUntilAsync(() => viewModel.HasError);

        Assert.AreEqual("iRacing unavailable", viewModel.ConnectionStatus);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.ErrorMessage);
        Assert.AreEqual(callsBeforeUpdate, service.StatusCalls);
        Assert.IsTrue(viewModel.EventLog[^1].IsError);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.EventLog[^1].Message);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("iRacing unavailable", viewModel.ConnectionStatus);
        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.ErrorMessage);
        Assert.AreEqual(callsBeforeUpdate + 1, service.StatusCalls);

        await viewModel.SavePreferencesAsync(CancellationToken.None);

        Assert.AreEqual(ApplicationErrors.ReplayUnavailable.Message, viewModel.ErrorMessage);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-LIF-001")]
    public async Task DisposeWaitsForAnUncooperativeActiveOperationBeforeDisposingItsGate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeIncidentReviewService
        {
            StatusHandler = async _ =>
            {
                entered.SetResult();
                await release.Task;
                return Result<ReviewServiceStatus>.Success(ReviewServiceStatus.Connected);
            },
        };
        var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await entered.Task;

        var dispose = viewModel.DisposeAsync().AsTask();
        Assert.IsFalse(dispose.IsCompleted);
        release.SetResult();

        await refresh;
        await dispose;
        Assert.IsFalse(viewModel.IsBusy);
        await viewModel.DisposeAsync();
    }

    private static async Task AssertAutomaticRefreshPreservesInteractionStateAsync(
        Func<IncidentReview.Domain.SessionIdentity, IncidentReview.Domain.IncidentId, ReviewUpdate> createUpdate)
    {
        var service = new FakeIncidentReviewService();
        var sessionId = IncidentReview.Domain.SessionIdentity.Generate();
        var selectedIncident = TestModelFactory.Incident(sessionId, 1_000, 7_000, 2, 2);
        var initialSession = TestModelFactory.Session(sessionId, selectedIncident);
        service.CurrentSessionResult = Result<ReviewSession>.Success(initialSession);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(initialSession)]);
        await using var viewModel = new MainWindowViewModel(service, new RecordingDispatcher());
        await viewModel.RefreshAsync(CancellationToken.None);
        var originalListItem = viewModel.Incidents[0];
        viewModel.SelectedIncident = originalListItem;
        viewModel.ReplayLeadInMilliseconds = "7000";
        viewModel.PlaybackSpeed = "0.75";
        viewModel.AutoPause = false;
        viewModel.PreferredCamera = "TV2";

        var addedIncident = TestModelFactory.Incident(sessionId, 2_000, 12_000, 4, 2);
        var refreshedSession = TestModelFactory.Session(
            sessionId,
            selectedIncident,
            addedIncident);
        service.CurrentSessionResult = Result<ReviewSession>.Success(refreshedSession);
        service.SessionsResult = Result<IReadOnlyList<SessionSummary>>.Success(
            [TestModelFactory.Summary(refreshedSession)]);
        service.PreferencesResult = Result<IncidentReview.Domain.UserPreferences>.Success(
            TestModelFactory.Preferences(1_000, 0.25, true, "Cockpit"));
        await viewModel.StartMonitoringAsync(CancellationToken.None);

        await service.PublishAsync(createUpdate(sessionId, selectedIncident.Id));
        await WaitUntilAsync(() => viewModel.Incidents.Count == 2);

        Assert.AreEqual("7000", viewModel.ReplayLeadInMilliseconds);
        Assert.AreEqual("0.75", viewModel.PlaybackSpeed);
        Assert.IsFalse(viewModel.AutoPause);
        Assert.AreEqual("TV2", viewModel.PreferredCamera);
        Assert.IsNotNull(viewModel.SelectedIncident);
        Assert.AreEqual(selectedIncident.Id, viewModel.SelectedIncident.Id);
        Assert.AreNotSame(originalListItem, viewModel.SelectedIncident);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
