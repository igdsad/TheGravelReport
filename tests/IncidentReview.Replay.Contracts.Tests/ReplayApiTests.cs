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
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ContextReaderExposesOneSynchronousRead()
    {
        var methods = typeof(IReplayContextReader).GetMethods();

        Assert.HasCount(1, methods);
        Assert.AreEqual(nameof(IReplayContextReader.Read), methods[0].Name);
        Assert.AreEqual(typeof(Result<ReplayContext>), methods[0].ReturnType);
        Assert.IsEmpty(methods[0].GetParameters());
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
                typeof(IReplayContextReader),
                typeof(ReplayErrorCodes),
                typeof(ReplayContext),
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
            candidate => candidate.Name == nameof(IReplayController.FocusParticipantAsync));
        Assert.AreEqual(typeof(ValueTask<Result>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.HasCount(3, parameters);
        Assert.AreEqual(typeof(IncidentParticipant), parameters[0].ParameterType);
        Assert.IsFalse(parameters[0].IsOptional);
        Assert.AreEqual(typeof(string), parameters[1].ParameterType);
        Assert.IsFalse(parameters[1].IsOptional);
        Assert.AreEqual(typeof(CancellationToken), parameters[2].ParameterType);
        Assert.IsFalse(parameters[2].IsOptional);
    }
}
