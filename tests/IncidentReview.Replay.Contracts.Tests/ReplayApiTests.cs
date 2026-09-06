namespace IncidentReview.Replay.Contracts.Tests;

[TestClass]
public sealed class ReplayApiTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "IR-RPY-003")]
    [TestProperty("Requirement", "QR-ERR-002")]
    public void ControllerExposesPreflightAndConfirmedAsyncCommands()
    {
        var methods = typeof(IReplayController).GetMethods();
        Assert.HasCount(4, methods);

        AssertValidationMethod(methods);
        AssertAsyncMethod(methods, nameof(IReplayController.SeekAsync), typeof(ReplayPosition));
        AssertAsyncMethod(methods, nameof(IReplayController.SetPlaybackAsync), typeof(ReplayPlayback));
        AssertFocusMethod(methods);
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

    private static void AssertValidationMethod(
        IEnumerable<System.Reflection.MethodInfo> methods)
    {
        var method = methods.Single(
            candidate => candidate.Name == nameof(IReplayController.ValidatePlayback));
        Assert.AreEqual(typeof(Result), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.HasCount(1, parameters);
        Assert.AreEqual(typeof(ReplayPlayback), parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
    }

    private static void AssertAsyncMethod(
        IEnumerable<System.Reflection.MethodInfo> methods,
        string methodName,
        Type intentType)
    {
        var method = methods.Single(candidate => candidate.Name == methodName);
        Assert.AreEqual(typeof(ValueTask<Result>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.HasCount(2, parameters);
        Assert.AreEqual(intentType, parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
        Assert.AreEqual(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.IsFalse(parameters[1].IsOptional);
        Assert.IsFalse(parameters[1].HasDefaultValue);
    }

    private static void AssertFocusMethod(
        IEnumerable<System.Reflection.MethodInfo> methods)
    {
        var method = methods.Single(
            candidate => candidate.Name == nameof(IReplayController.FocusPlayerAsync));
        Assert.AreEqual(typeof(ValueTask<Result>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.HasCount(2, parameters);
        Assert.AreEqual(typeof(string), parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
        Assert.AreEqual(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.IsFalse(parameters[1].IsOptional);
    }
}
