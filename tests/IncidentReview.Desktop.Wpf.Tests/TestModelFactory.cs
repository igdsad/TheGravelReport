using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf.Tests;

internal static class TestModelFactory
{
    public static ReviewSnapshot Snapshot(
        long revision = 0,
        ReviewServiceStatus status = ReviewServiceStatus.Connected,
        ReviewSession? activeSession = null,
        UserPreferences? preferences = null,
        string? driverDisplayName = null,
        IEnumerable<string>? cameraGroups = null,
        string? currentCameraGroup = null,
        IncidentReview.Results.Error? statusError = null) =>
        ReviewSnapshot.Create(
            revision,
            status,
            statusError,
            activeSession,
            preferences ?? Preferences(),
            driverDisplayName,
            cameraGroups ?? Array.Empty<string>(),
            currentCameraGroup);

    public static UserPreferences Preferences(
        long leadIn = 3_000,
        double speed = 0.5,
        bool autoPause = true,
        string? camera = "Cockpit",
        ThemePreference theme = ThemePreference.FollowDesktop) =>
        UserPreferences.TryCreateMilliseconds(leadIn, speed, autoPause, camera, theme).Value;

    public static ReviewSession Session(
        SessionIdentity? id = null,
        params ReviewIncident[] incidents)
    {
        var identity = id ?? SessionIdentity.Generate();
        return ReviewSession.Create(
            identity,
            Descriptor(),
            UtcInstant.TryCreateUnixMilliseconds(1_000).Value,
            incidents);
    }

    public static SessionSummary Summary(ReviewSession session) => SessionSummary.Create(
        session.Id,
        session.Descriptor,
        session.StartedAt,
        session.Incidents.Count,
        session.Incidents.Count(static incident => incident.ReviewStatus == IncidentReviewStatus.Pending));

    public static ReviewIncident Incident(
        SessionIdentity session,
        long observedAt,
        long replayTime,
        int total,
        int delta,
        IncidentParticipant? participant = null) => ReviewIncident.Create(
            IncidentId.Generate(),
            session,
            participant ?? Participant(),
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(1).Value,
                SessionTime.TryCreateMilliseconds(replayTime).Value).Value,
            UtcInstant.TryCreateUnixMilliseconds(observedAt).Value,
            IncidentPoints.TryCreate(total, delta).Value,
            CounterEpoch.TryCreate(0).Value,
            LapNumber.TryCreate(2).Value,
            LapDistance.TryCreate(0.25).Value,
            IncidentReviewStatus.Pending,
            IncidentAnnotation.TryCreate(null, null).Value);

    public static IncidentParticipant Participant(
        string identity = "car-index:2:team:22",
        string? driverName = "Test Driver",
        string? teamName = "Test Team",
        string? carNumber = "22") => IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate(identity).Value,
            driverName,
            teamName,
            carNumber).Value;

    private static SimulatorSessionDescriptor Descriptor() => SimulatorSessionDescriptor.TryCreate(
        SimulatorCode.TryCreate("iracing").Value,
        SimulatorSessionKey.TryCreate(Guid.NewGuid().ToString("D")).Value,
        SessionNumber.TryCreate(1).Value,
        SessionMode.Live,
        SimulatorIdentityScope.Durable).Value;
}
