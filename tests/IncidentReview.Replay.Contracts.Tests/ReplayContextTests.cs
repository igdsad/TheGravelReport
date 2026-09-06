namespace IncidentReview.Replay.Contracts.Tests;

[TestClass]
public sealed class ReplayContextTests
{
    private static readonly string[] ExpectedCameraGroups = ["Cockpit", "TV1", "Scenic"];
    private static readonly string[] ExpectedSnapshotGroups = ["Cockpit", "TV1"];

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void TryCreateNormalizesDriverAndPreservesSimulatorCameraOrder()
    {
        var decomposedName = " Rene\u0301 ";
        string[] groups = [" Cockpit ", "TV1", "Scenic"];

        var result = ReplayContext.TryCreate(decomposedName, groups, "tv1");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("René", result.Value.DriverDisplayName);
        CollectionAssert.AreEqual(
            ExpectedCameraGroups,
            result.Value.CameraGroups.ToArray());
        Assert.AreEqual("TV1", result.Value.CurrentCameraGroup);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    public void ContextAllowsIndependentlyUnavailableDriverAndCameraMetadata()
    {
        var driverOnly = ReplayContext.TryCreate("Driver", [], currentCameraGroup: null);
        var camerasOnly = ReplayContext.TryCreate(null, ["TV1"], currentCameraGroup: null);
        var empty = ReplayContext.TryCreate(null, [], currentCameraGroup: null);

        Assert.IsTrue(driverOnly.IsSuccess);
        Assert.AreEqual("Driver", driverOnly.Value.DriverDisplayName);
        Assert.IsEmpty(driverOnly.Value.CameraGroups);
        Assert.IsTrue(camerasOnly.IsSuccess);
        Assert.IsNull(camerasOnly.Value.DriverDisplayName);
        Assert.HasCount(1, camerasOnly.Value.CameraGroups);
        Assert.IsTrue(empty.IsSuccess);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ContextDefensivelySnapshotsItsCameraCatalog()
    {
        string[] groups = ["Cockpit", "TV1"];
        var context = ReplayContext.TryCreate("Driver", groups, "Cockpit").Value;

        groups[0] = "Mutated";

        CollectionAssert.AreEqual(
            ExpectedSnapshotGroups,
            context.CameraGroups.ToArray());
        Assert.IsNull(typeof(ReplayContext).GetProperty(nameof(ReplayContext.CameraGroups))!.SetMethod);
        Assert.IsEmpty(typeof(ReplayContext).GetConstructors());
        Assert.IsTrue(typeof(ReplayContext).IsSealed);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("blank-group")]
    [DataRow("duplicate-group")]
    [DataRow("unknown-current")]
    [DataRow("driver-control")]
    [DataRow("group-control")]
    [DataRow("invalid-surrogate")]
    [DataRow("too-many-groups")]
    [DataRow("null-groups")]
    public void TryCreateRejectsEveryInvalidContextDimension(string dimension)
    {
        string? driver = "Driver";
        IEnumerable<string>? groups = ["Cockpit", "TV1"];
        string? current = "Cockpit";
        switch (dimension)
        {
            case "blank-group":
                groups = ["Cockpit", " "];
                break;
            case "duplicate-group":
                groups = ["TV1", "tv1"];
                break;
            case "unknown-current":
                current = "TV3";
                break;
            case "driver-control":
                driver = "Driver\nName";
                break;
            case "group-control":
                groups = ["TV\t1"];
                current = null;
                break;
            case "invalid-surrogate":
                driver = "Driver\ud800";
                break;
            case "too-many-groups":
                groups = Enumerable.Range(0, ReplayContext.MaximumCameraGroupCount + 1)
                    .Select(static index => $"Camera {index}");
                current = null;
                break;
            case "null-groups":
                groups = null;
                current = null;
                break;
            default:
                Assert.Fail($"Unknown test dimension: {dimension}");
                break;
        }

        var result = ReplayContext.TryCreate(driver, groups, current);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ReplayErrorCodes.InvalidContext, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error?.Kind);
    }
}
