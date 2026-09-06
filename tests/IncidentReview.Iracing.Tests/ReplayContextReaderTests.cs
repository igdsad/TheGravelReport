using IncidentReview.Domain;
using IncidentReview.Iracing.Testing;
using IncidentReview.Replay.Contracts;
using IncidentReview.Results;

namespace IncidentReview.Iracing.Tests;

[TestClass]
public sealed class ReplayContextReaderTests
{
    private static readonly string[] ExpectedCameraGroups = ["Cockpit", "TV1"];

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ReadReturnsUnicodeDriverOrderedGroupsAndCurrentGroupWithoutSdkNumbers()
    {
        var integration = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: ValidContextSessionInfo);
        integration.ObserveFrame(State(ValidContextSessionInfo, cameraGroupNumber: 2));

        var result = integration.ContextReader.Read();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("René Gravel", result.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ExpectedCameraGroups,
            result.Value.CameraGroups.ToArray());
        Assert.AreEqual("TV1", result.Value.CurrentCameraGroup);
        Assert.IsTrue(typeof(ReplayContext).GetProperties().All(property =>
            property.PropertyType != typeof(int) && property.PropertyType != typeof(int?)));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    public async Task MissingOrDuplicateDisplayNamePreservesCamerasAndPlayerFocus()
    {
        var missing = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: SessionInfoWithDriverEntry(""));
        var duplicate = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: SessionInfoWithDriverEntry(
                "   UserName: First Driver\n   UserName: Second Driver\n"));

        var missingContext = missing.ContextReader.Read();
        var duplicateContext = duplicate.ContextReader.Read();
        var missingFocus = await missing.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None);
        var duplicateFocus = await duplicate.Controller.FocusParticipantAsync(
            LocalPlayer(),
            "TV1",
            CancellationToken.None);

        Assert.IsTrue(missingContext.IsSuccess);
        Assert.IsNull(missingContext.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ExpectedCameraGroups,
            missingContext.Value.CameraGroups.ToArray());
        Assert.IsTrue(duplicateContext.IsSuccess);
        Assert.IsNull(duplicateContext.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ExpectedCameraGroups,
            duplicateContext.Value.CameraGroups.ToArray());
        Assert.IsTrue(missingFocus.IsSuccess);
        Assert.IsTrue(duplicateFocus.IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [DataRow("   CarNumberRaw: invalid\n")]
    [DataRow("   CarNumberRaw: 23\n - CarIdx: 4\n   CarNumberRaw: 24\n")]
    [DataRow("   this is not a mapping\n")]
    public void MalformedPlayerMetadataDoesNotSuppressValidCameraCatalog(string playerEntry)
    {
        var integration = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: SessionInfoWithRawPlayerEntry(playerEntry));

        var result = integration.ContextReader.Read();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ExpectedCameraGroups,
            result.Value.CameraGroups.ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [DataRow("   - CameraNum: invalid\n")]
    [DataRow(" - GroupNum: 1\n   GroupName: Cockpit\n   Cameras:\n   - CameraNum: 11\n")]
    [DataRow("   this is not a mapping\n")]
    public void MalformedCameraMetadataDoesNotSuppressValidDriver(string cameraBody)
    {
        var sessionInfo = string.Concat(
            SessionHeader,
            "DriverInfo:\n",
            " DriverCarIdx: 4\n",
            " Drivers:\n",
            " - CarIdx: 4\n",
            "   UserName: René Gravel\n",
            "   CarNumberRaw: 23\n",
            "CameraInfo:\n",
            " Groups:\n",
            " - GroupNum: 1\n",
            "   GroupName: Cockpit\n",
            "   Cameras:\n",
            cameraBody);
        var integration = IracingTestingRegistration.CreateReplayContext(sessionInfo: sessionInfo);

        var result = integration.ContextReader.Read();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("René Gravel", result.Value.DriverDisplayName);
        Assert.IsEmpty(result.Value.CameraGroups);
        Assert.IsNull(result.Value.CurrentCameraGroup);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public void ReadReportsExactUnavailableFailureAfterDisconnect()
    {
        var integration = IracingTestingRegistration.CreateReplayContext(
            sessionInfo: ValidContextSessionInfo);
        integration.Disconnect();

        var result = integration.ContextReader.Read();

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IracingErrorCodes.ReplayUnavailable, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Unavailable, result.Error?.Kind);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-CON-001")]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void LaterValidFrameAtomicallyRestoresReplayContext()
    {
        var integration = IracingTestingRegistration.CreateReplayContext(
            telemetryAvailable: false,
            sessionInfo: ValidContextSessionInfo);
        Assert.IsFalse(integration.ContextReader.Read().IsSuccess);

        integration.ObserveFrame(State(ValidContextSessionInfo, cameraGroupNumber: 2));

        var recovered = integration.ContextReader.Read();
        Assert.IsTrue(recovered.IsSuccess);
        Assert.AreEqual("René Gravel", recovered.Value.DriverDisplayName);
        Assert.AreEqual("TV1", recovered.Value.CurrentCameraGroup);
    }

    private static string SessionInfoWithDriverEntry(string displayNameLines) => string.Concat(
        SessionHeader,
        "DriverInfo:\n",
        " DriverCarIdx: 4\n",
        " Drivers:\n",
        " - CarIdx: 4\n",
        displayNameLines,
        "   CarNumberRaw: 23\n",
        CameraInfo);

    private static string SessionInfoWithRawPlayerEntry(string playerEntry) => string.Concat(
        SessionHeader,
        "DriverInfo:\n",
        " DriverCarIdx: 4\n",
        " Drivers:\n",
        " - CarIdx: 4\n",
        "   UserName: René Gravel\n",
        playerEntry,
        CameraInfo);

    private static IracingTestReplayState State(
        string sessionInfo,
        int cameraGroupNumber) => new(
        ReplaySessionNumber: 0,
        ReplaySessionTimeMilliseconds: 0,
        ReplayPlaySpeed: 1,
        ReplayPlaySlowMotion: false,
        CameraState: 1,
        CameraCarIndex: 4,
        CameraGroupNumber: cameraGroupNumber,
        CameraNumber: 20,
        SessionInfo: sessionInfo);

    private const string ValidContextSessionInfo = """
        SessionInfo:
         CurrentSessionNum: 0
        DriverInfo:
         DriverCarIdx: 4
         Drivers:
         - CarIdx: 2
           UserName: Other Driver
           CarNumberRaw: 12
         - CarIdx: 4
           UserName: René Gravel
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
        """;

    private const string SessionHeader = "SessionInfo:\n CurrentSessionNum: 0\n";

    private const string CameraInfo = """
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
        """;

    private static IncidentParticipant LocalPlayer() =>
        IncidentParticipant.TryCreate(
            ParticipantIdentity.TryCreate("local-player").Value,
            driverName: null,
            teamName: null,
            carNumber: null).Value;
}
