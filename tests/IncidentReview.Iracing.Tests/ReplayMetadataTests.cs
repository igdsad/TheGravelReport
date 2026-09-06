using IncidentReview.Iracing.Testing;

namespace IncidentReview.Iracing.Tests;

[TestClass]
public sealed class ReplayMetadataTests
{
    private static readonly int[] TvCameraNumbers = [20, 21];

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ParserReadsOfficialDriverAndOrderedCameraShape()
    {
        var metadata = IracingTestingRegistration.ReadReplayMetadata(OfficialSessionInfo);

        Assert.IsNotNull(metadata);
        Assert.AreEqual(4, metadata.PlayerCarIndex);
        Assert.AreEqual(23, metadata.PlayerCarNumberRaw);
        Assert.HasCount(2, metadata.CameraGroups);
        Assert.AreEqual(2, metadata.CameraGroups[1].Number);
        Assert.AreEqual("TV1", metadata.CameraGroups[1].Name);
        CollectionAssert.AreEqual(
            TvCameraNumbers,
            metadata.CameraGroups[1].CameraNumbers.ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("")]
    [DataRow("DriverInfo:\n DriverCarIdx: 4")]
    [DataRow("DriverInfo:\n\tDriverCarIdx: 4\nCameraInfo:")]
    [DataRow("""
        DriverInfo:
         DriverCarIdx: 4
         DriverCarIdx: 5
         Drivers:
         - CarIdx: 4
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 2
           GroupName: TV1
           Cameras:
           - CameraNum: 20
        """)]
    [DataRow("""
        DriverInfo:
         DriverCarIdx: 4
         Drivers:
         - CarIdx: 4
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 2
           GroupName: TV1
           Cameras:
        """)]
    public void ParserFailsClosedForMissingMalformedOrAmbiguousMetadata(string sessionInfo)
    {
        Assert.IsNull(IracingTestingRegistration.ReadReplayMetadata(sessionInfo));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    public void PausedSessionScreenStillClassifiesAsReplay()
    {
        Assert.IsTrue(IracingTestingRegistration.IsReplayMode(
            isReplayPlaying: false,
            cameraState: 0x0001));
        Assert.IsFalse(IracingTestingRegistration.IsReplayMode(
            isReplayPlaying: false,
            cameraState: 0));
        Assert.IsTrue(IracingTestingRegistration.IsReplayMode(
            isReplayPlaying: true,
            cameraState: 0));
    }

    private const string OfficialSessionInfo = """
        ---
        WeekendInfo:
         SubSessionID: 987654321
        DriverInfo:
         DriverCarIdx: 4
         Drivers:
         - CarIdx: 2
           CarNumberRaw: 12
         - CarIdx: 4
           CarNumberRaw: 23
        CameraInfo:
         Groups:
         - GroupNum: 1
           GroupName: Cockpit
           Cameras:
           - CameraNum: 10
         - GroupNum: 2
           GroupName: TV1
           Cameras:
           - CameraNum: 20
           - CameraNum: 21
        ...
        """;
}
