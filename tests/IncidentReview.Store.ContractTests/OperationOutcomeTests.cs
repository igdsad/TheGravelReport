using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class OperationOutcomeTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    public void OutcomeHasExactlyCommittedAndNotCommittedStates()
    {
        Assert.IsTrue(OperationOutcome.Committed.IsCommitted);
        Assert.IsFalse(OperationOutcome.NotCommitted.IsCommitted);
        Assert.AreNotEqual(OperationOutcome.Committed, OperationOutcome.NotCommitted);

        var publicStates = typeof(OperationOutcome).GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)
            .Select(property => property.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[] { nameof(OperationOutcome.Committed), nameof(OperationOutcome.NotCommitted) },
            publicStates);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-006")]
    public void ReconciliationQueryIsTypedAndImmutable()
    {
        var operationId = OperationId.Create();
        var query = new GetOperationOutcome(operationId);

        Assert.AreSame(operationId, query.OperationId);
        Assert.IsInstanceOfType<IStoreQuery<OperationOutcome>>(query);
        Assert.IsNull(typeof(GetOperationOutcome)
            .GetProperty(nameof(GetOperationOutcome.OperationId))!
            .SetMethod);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ReconciliationQueryRejectsANullIdentity()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new GetOperationOutcome(null!));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void OutcomeCannotBeConstructedOrMutatedIntoAnotherState()
    {
        Assert.IsTrue(typeof(OperationOutcome).IsSealed);
        Assert.IsEmpty(typeof(OperationOutcome).GetConstructors());
        Assert.IsNull(typeof(OperationOutcome)
            .GetProperty(nameof(OperationOutcome.IsCommitted))!
            .SetMethod);
    }
}
