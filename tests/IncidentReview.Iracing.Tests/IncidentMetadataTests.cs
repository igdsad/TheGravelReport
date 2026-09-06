using IncidentReview.Iracing.Testing;

namespace IncidentReview.Iracing.Tests;

[TestClass]
public sealed class IncidentMetadataTests
{
    private static readonly int[] ExpectedEligibleCarIndexes = [2, 4];

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "IR-INC-007")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DecoderUsesScoredTeamCountersAndDeterministicIdentityEvidencePerEligibleCar()
    {
        var metadata = IracingTestingRegistration.ReadIncidentMetadata(SessionInfo);

        Assert.AreEqual(3, metadata.CurrentSessionNumber);
        Assert.AreEqual(4, metadata.PlayerCarIndex);
        CollectionAssert.AreEqual(
            ExpectedEligibleCarIndexes,
            metadata.Participants.Select(static participant => participant.CarIndex).ToArray());

        var team = metadata.Participants[0];
        Assert.AreEqual("car-index:2:team:88", team.Identity);
        Assert.AreEqual(4, team.IncidentCount);
        Assert.AreEqual("First Driver", team.DriverName);
        Assert.AreEqual("Example Team", team.TeamName);
        Assert.AreEqual("007", team.CarNumber);
        Assert.AreEqual(7, team.CarNumberRaw);

        Assert.AreEqual("car-index:4:user:404", metadata.Participants[1].Identity);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    public void TeamIdentitySurvivesCurrentDriverSwap()
    {
        var before = IracingTestingRegistration.ReadIncidentMetadata(SessionInfo);
        var after = IracingTestingRegistration.ReadIncidentMetadata(
            SessionInfo.Replace(
                "UserID: 202\n   UserName: First Driver",
                "UserID: 909\n   UserName: Relief Driver",
                StringComparison.Ordinal));

        Assert.AreEqual(before.Participants[0].Identity, after.Participants[0].Identity);
        Assert.AreEqual("Relief Driver", after.Participants[0].DriverName);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-006")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void IdentityEvidenceLossIsOmittedAndCanonicalIdentityIsRestartDeterministic()
    {
        var initial = IracingTestingRegistration.ReadIncidentMetadata(SessionInfo);
        var missing = IracingTestingRegistration.ReadIncidentMetadata(
            SessionInfo.Replace("   TeamID: 88\n", string.Empty, StringComparison.Ordinal));
        var malformed = IracingTestingRegistration.ReadIncidentMetadata(
            SessionInfo.Replace("TeamID: 88", "TeamID: invalid", StringComparison.Ordinal));
        var changed = IracingTestingRegistration.ReadIncidentMetadata(
            SessionInfo.Replace("TeamID: 88", "TeamID: 89", StringComparison.Ordinal));
        var recreated = IracingTestingRegistration.ReadIncidentMetadata(SessionInfo);

        Assert.IsFalse(missing.Participants.Any(participant => participant.CarIndex == 2));
        Assert.IsFalse(malformed.Participants.Any(participant => participant.CarIndex == 2));
        Assert.AreEqual("car-index:2:team:89", changed.Participants[0].Identity);
        Assert.AreEqual(initial.Participants[0].Identity, recreated.Participants[0].Identity);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-INC-007")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void InvalidCarIndexesAreSkippedButDuplicateValidIndexesFailClosed()
    {
        var withInvalidEntries = IracingTestingRegistration.ReadIncidentMetadata(SessionInfo);
        var duplicate = IracingTestingRegistration.ReadIncidentMetadata(string.Concat(
            SessionInfo,
            "\n - CarIdx: 2\n   TeamIncidentCount: 5\n"));

        CollectionAssert.AreEqual(
            ExpectedEligibleCarIndexes,
            withInvalidEntries.Participants
                .Select(static participant => participant.CarIndex)
                .ToArray());
        Assert.IsEmpty(duplicate.Participants);
    }

    private const string SessionInfo = """
        SessionInfo:
         CurrentSessionNum: 3
        DriverInfo:
         DriverCarIdx: 4
         PaceCarIdx: 0
         Drivers:
         - CarIdx: 6
           TeamID: 0
           UserID: 0
           TeamIncidentCount: 0
         - CarIdx: 3
           TeamID: 33
           UserID: 303
           IsSpectator: 1
           TeamIncidentCount: 8
         - CarIdx: 2
           TeamID: 88
           UserID: 202
           UserName: First Driver
           TeamName: Example Team
           CarNumber: "007"
           CarNumberRaw: 7
           TeamIncidentCount: 4
         - CarIdx: 8
           TeamID: 80
           UserID: 808
           TeamIncidentCount: -1
         - CarIdx: 4
           TeamID: 0
           UserID: 404
           TeamIncidentCount: 9
         - CarIdx: 0
           CarIsPaceCar: 0
           TeamIncidentCount: 0
         - CarIdx: 7
           TeamID: 70
           UserID: 707
         - CarIdx: invalid
           TeamIncidentCount: 99
         - CarIdx: -1
           TeamIncidentCount: 99
         - CarIdx: 64
           TeamIncidentCount: 99
        """;
}
