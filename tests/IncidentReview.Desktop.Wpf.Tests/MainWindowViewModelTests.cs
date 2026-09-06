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
    public async Task ReviewSelectedIncidentInvokesApplicationAndShowsActionableFailure()
    {
        var service = new FakeIncidentReviewService
        {
            ReviewResult = Result.Failure(ApplicationErrors.ReplayUnavailable),
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
        StringAssert.Contains(viewModel.ErrorMessage, "Check that iRacing is running");
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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
