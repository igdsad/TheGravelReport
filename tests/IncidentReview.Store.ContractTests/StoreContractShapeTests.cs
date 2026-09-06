using System.Reflection;
using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class StoreContractShapeTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void StoreQueryResultIsCovariant()
    {
        var resultParameter = typeof(IStoreQuery<>).GetGenericArguments().Single();
        var variance = resultParameter.GenericParameterAttributes &
            GenericParameterAttributes.VarianceMask;

        Assert.AreEqual(GenericParameterAttributes.Covariant, variance);

        IStoreQuery<string> specificQuery = new StringQuery();
        IStoreQuery<object> generalQuery = specificQuery;
        Assert.AreSame(specificQuery, generalQuery);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void StoreExposesOnlyTypedQueriesAndAtomicCommands()
    {
        var methods = typeof(IStore).GetMethods();

        Assert.HasCount(2, methods);

        var query = methods.Single(method => method.Name == nameof(IStore.QueryAsync));
        Assert.IsTrue(query.IsGenericMethodDefinition);
        Assert.AreEqual(typeof(IStoreQuery<>), query.GetParameters()[0].ParameterType.GetGenericTypeDefinition());
        Assert.AreEqual(typeof(CancellationToken), query.GetParameters()[1].ParameterType);
        Assert.AreEqual(typeof(Task<>), query.ReturnType.GetGenericTypeDefinition());
        Assert.AreEqual(typeof(Result<>), query.ReturnType.GetGenericArguments()[0].GetGenericTypeDefinition());

        var execute = methods.Single(method => method.Name == nameof(IStore.ExecuteAsync));
        Assert.AreEqual(typeof(IStoreCommand), execute.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(CancellationToken), execute.GetParameters()[1].ParameterType);
        Assert.AreEqual(typeof(Task<Result>), execute.ReturnType);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void StoreCommandRequiresExactlyOneOperationIdentityCapability()
    {
        var properties = typeof(IStoreCommand).GetProperties();

        Assert.HasCount(1, properties);
        Assert.AreEqual(nameof(IStoreCommand.OperationId), properties[0].Name);
        Assert.AreEqual(typeof(OperationId), properties[0].PropertyType);
        Assert.IsNull(properties[0].SetMethod);
        Assert.IsEmpty(typeof(IStoreCommand).GetMethods().Where(method => !method.IsSpecialName));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void InitializerIsSeparateFromOrdinaryStoreOperations()
    {
        Assert.IsFalse(typeof(IStoreInitializer).IsAssignableFrom(typeof(IStore)));

        var methods = typeof(IStoreInitializer).GetMethods();
        Assert.HasCount(1, methods);

        var method = methods[0];
        Assert.AreEqual(nameof(IStoreInitializer.InitializeAsync), method.Name);
        Assert.AreEqual(typeof(Task<Result>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.HasCount(1, parameters);
        Assert.AreEqual(typeof(CancellationToken), parameters[0].ParameterType);
    }

    private sealed class StringQuery : IStoreQuery<string>
    {
    }
}
