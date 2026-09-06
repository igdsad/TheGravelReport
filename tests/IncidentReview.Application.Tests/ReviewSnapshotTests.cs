using IncidentReview.Application.Contracts;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Application.Tests;

[TestClass]
public sealed class ReviewSnapshotTests
{
    private static readonly UserPreferences DefaultPreferences =
        UserPreferences.TryCreateMilliseconds(5_000, 1, autoPause: true, preferredCamera: null).Value;
    private static readonly string[] ExpectedCameraGroups = ["Cockpit", "TV1"];

    [TestMethod]
    [TestProperty("Requirement", "IR-UI-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void CreateNormalizesAndDefensivelySnapshotsReplayContext()
    {
        string[] groups = [" Cockpit ", "TV1"];

        var snapshot = ReviewSnapshot.Create(
            revision: 4,
            ReviewServiceStatus.Connected,
            statusError: null,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: " Rene\u0301 ",
            groups,
            currentCameraGroup: "tv1");
        groups[0] = "Mutated";

        Assert.AreEqual(4, snapshot.Revision);
        Assert.AreEqual("René", snapshot.DriverDisplayName);
        CollectionAssert.AreEqual(ExpectedCameraGroups, snapshot.CameraGroups.ToArray());
        Assert.AreEqual("TV1", snapshot.CurrentCameraGroup);
        Assert.IsNull(typeof(ReviewSnapshot).GetProperty(nameof(ReviewSnapshot.CameraGroups))!.SetMethod);
        Assert.IsEmpty(typeof(ReviewSnapshot).GetConstructors());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateRequiresAnExactErrorOnlyForUnavailableStatus()
    {
        var error = Error.Create(
            ErrorCode.Define("test.snapshot.unavailable"),
            ErrorKind.Unavailable,
            "Snapshot unavailable.");

        Assert.ThrowsExactly<ArgumentException>(() => ReviewSnapshot.Create(
            0,
            ReviewServiceStatus.Unavailable,
            statusError: null,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: [],
            currentCameraGroup: null));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewSnapshot.Create(
            0,
            ReviewServiceStatus.Connected,
            error,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: [],
            currentCameraGroup: null));

        var snapshot = ReviewSnapshot.Create(
            0,
            ReviewServiceStatus.Unavailable,
            error,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: [],
            currentCameraGroup: null);

        Assert.AreSame(error, snapshot.StatusError);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void CreateRejectsInvalidRevisionAndCameraRelationships()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReviewSnapshot.Create(
            -1,
            ReviewServiceStatus.Stopped,
            statusError: null,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: [],
            currentCameraGroup: null));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewSnapshot.Create(
            0,
            ReviewServiceStatus.Connected,
            statusError: null,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: ["TV1"],
            currentCameraGroup: "TV2"));
        Assert.ThrowsExactly<ArgumentException>(() => ReviewSnapshot.Create(
            0,
            ReviewServiceStatus.Connected,
            statusError: null,
            activeSession: null,
            DefaultPreferences,
            driverDisplayName: null,
            cameraGroups: ["TV1", "tv1"],
            currentCameraGroup: null));
    }
}
