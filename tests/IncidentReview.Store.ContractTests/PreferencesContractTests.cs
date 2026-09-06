using IncidentReview.Domain;
using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class PreferencesContractTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SET-001")]
    public void PreferencesQueryIsAStatelessImmutableSingleton()
    {
        var instanceProperty = typeof(GetPreferences).GetProperty(nameof(GetPreferences.Instance))!;
        Assert.AreSame(instanceProperty.GetValue(null), instanceProperty.GetValue(null));
        Assert.IsInstanceOfType<IStoreQuery<UserPreferences>>(GetPreferences.Instance);
        Assert.IsEmpty(typeof(GetPreferences).GetConstructors());
        Assert.IsEmpty(typeof(GetPreferences).GetProperties(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-005")]
    public void UpdateFactoryGeneratesAndRetainsOneRetryIdentity()
    {
        var preferences = CreatePreferences();
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(1_234_567).Value;

        var first = UpdatePreferences.Create(preferences, updatedAt);
        var second = UpdatePreferences.Create(preferences, updatedAt);

        Assert.AreSame(preferences, first.Preferences);
        Assert.AreSame(updatedAt, first.UpdatedAt);
        Assert.AreNotEqual(first.OperationId, second.OperationId);
        Assert.IsInstanceOfType<IStoreCommand>(first);
        Assert.IsEmpty(typeof(UpdatePreferences).GetConstructors());
        Assert.IsNull(typeof(UpdatePreferences).GetProperty(nameof(UpdatePreferences.OperationId))!.SetMethod);
        Assert.IsNull(typeof(UpdatePreferences).GetProperty(nameof(UpdatePreferences.Preferences))!.SetMethod);
        Assert.IsNull(typeof(UpdatePreferences).GetProperty(nameof(UpdatePreferences.UpdatedAt))!.SetMethod);
        var factory = typeof(UpdatePreferences).GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Single(method => method.Name == nameof(UpdatePreferences.Create));
        Assert.AreEqual(nameof(UpdatePreferences.Create), factory.Name);
        CollectionAssert.AreEqual(
            new[] { typeof(UserPreferences), typeof(UtcInstant) },
            factory.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void UpdateFactoryRejectsNullDomainValues()
    {
        var preferences = CreatePreferences();
        var updatedAt = UtcInstant.TryCreateUnixMilliseconds(0).Value;

        Assert.ThrowsExactly<ArgumentNullException>(() => UpdatePreferences.Create(null!, updatedAt));
        Assert.ThrowsExactly<ArgumentNullException>(() => UpdatePreferences.Create(preferences, null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void StoreFailuresHaveStableCodesAndRecoveryKinds()
    {
        var cases = new (Error Error, ErrorCode Code, ErrorKind Kind)[]
        {
            (StoreErrors.NotInitialized, StoreErrorCodes.NotInitialized, ErrorKind.Unavailable),
            (StoreErrors.UnsupportedRequest, StoreErrorCodes.UnsupportedRequest, ErrorKind.Validation),
            (StoreErrors.PersistenceFailure, StoreErrorCodes.PersistenceFailure, ErrorKind.Persistence),
            (StoreErrors.OperationIdConflict, StoreErrorCodes.OperationIdConflict, ErrorKind.Conflict),
            (StoreErrors.IndeterminateCommit, StoreErrorCodes.IndeterminateCommit, ErrorKind.Indeterminate),
        };

        foreach (var item in cases)
        {
            Assert.AreEqual(item.Code, item.Error.Code);
            Assert.AreEqual(item.Kind, item.Error.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Error.Message));
        }
    }

    private static UserPreferences CreatePreferences() =>
        UserPreferences.TryCreateMilliseconds(5_000, 0.5, autoPause: true, "Cockpit").Value;
}
