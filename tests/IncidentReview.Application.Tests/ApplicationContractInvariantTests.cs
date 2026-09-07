using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Application.Tests;

[TestClass]
public sealed class ApplicationContractInvariantTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void SessionIdentityErrorUsesProductNeutralContractLanguage()
    {
        Assert.AreEqual(
            "iRacing did not provide a stable ID for this replay, so the app cannot verify " +
            "that it matches the saved incident. Keep the app running while recording and " +
            "entering replay, then try again.",
            ApplicationErrors.ReplaySessionIdentityUnavailable.Message);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ReviewSessionRejectsIncidentsFromAnotherSession()
    {
        var session = SessionIdentity.Generate();
        var anotherSession = SessionIdentity.Generate();
        var instant = UtcInstant.TryCreateUnixMilliseconds(1_000).Value;
        var participant = IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate("car-index:2:team:22").Value,
            "Test Driver",
            "Test Team",
            "22").Value;
        var incident = ReviewIncident.Create(
            IncidentId.Generate(),
            anotherSession,
            participant,
            ReplayPosition.TryCreate(
                SessionNumber.TryCreate(1).Value,
                SessionTime.TryCreateMilliseconds(1_000).Value).Value,
            instant,
            IncidentPoints.TryCreate(total: 1, delta: 1).Value,
            CounterEpoch.TryCreate(0).Value,
            lap: null,
            lapDistance: null,
            IncidentReviewStatus.Pending,
            IncidentAnnotation.TryCreate(null, null).Value);

        Assert.AreSame(participant, incident.Participant);

        Assert.ThrowsExactly<ArgumentException>(() => ReviewSession.Create(
            session,
            SimulatorSessionDescriptor.TryCreate(
                SimulatorCode.TryCreate("iracing").Value,
                SimulatorSessionKey.TryCreate("session-key").Value,
                SessionNumber.TryCreate(1).Value,
                SessionMode.Live,
                SimulatorIdentityScope.Durable).Value,
            instant,
            [incident]));
    }
}
