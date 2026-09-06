namespace IncidentReview.Replay.Contracts.Tests;

[TestClass]
public sealed class ReplayApiTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public void ControllerExposesOnlyCancelableSeekAndPlaybackIntents()
    {
        var methods = typeof(IReplayController).GetMethods();
        Assert.HasCount(2, methods);

        AssertMethod(methods, nameof(IReplayController.SeekAsync), typeof(ReplayPosition));
        AssertMethod(methods, nameof(IReplayController.SetPlaybackAsync), typeof(ReplayPlayback));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void AssemblyExportsOnlyTheDeliberateContractSurface()
    {
        var exportedTypes = typeof(IReplayController).Assembly.GetExportedTypes();

        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(IReplayController),
                typeof(ReplayErrorCodes),
                typeof(ReplayPlayback),
            },
            exportedTypes);
    }

    private static void AssertMethod(
        IEnumerable<System.Reflection.MethodInfo> methods,
        string methodName,
        Type intentType)
    {
        var method = methods.Single(candidate => candidate.Name == methodName);
        Assert.AreEqual(typeof(Task<Result>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.HasCount(2, parameters);
        Assert.AreEqual(intentType, parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
        Assert.AreEqual(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.IsFalse(parameters[1].IsOptional);
        Assert.IsFalse(parameters[1].HasDefaultValue);
    }
}
