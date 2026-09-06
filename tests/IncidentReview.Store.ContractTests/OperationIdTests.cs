using IncidentReview.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class OperationIdTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void CreateGeneratesCanonicalUuidVersionSevenValues()
    {
        var values = Enumerable.Range(0, 256)
            .Select(_ => OperationId.Create().ToString())
            .ToArray();

        Assert.AreEqual(values.Length, values.Distinct(StringComparer.Ordinal).Count());

        foreach (var value in values)
        {
            Assert.AreEqual(36, value.Length);
            Assert.AreEqual('7', value[14]);
            Assert.IsTrue(value[19] is '8' or '9' or 'a' or 'b');
            Assert.AreEqual(value.ToLowerInvariant(), value);
            Assert.IsTrue(Guid.TryParseExact(value, "D", out _));
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ParseRehydratesTheSameImmutableIdentity()
    {
        var original = OperationId.Create();

        var parsed = OperationId.TryParse(original.ToString());

        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual(original, parsed.Value);
        Assert.IsTrue(original == parsed.Value);
        Assert.IsFalse(original != parsed.Value);
        Assert.AreEqual(original.GetHashCode(), parsed.Value.GetHashCode());
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ParseRejectsNullWithAStableValidationFailure()
    {
        var result = OperationId.TryParse(null);

        AssertInvalid(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    public void ParseRejectsUppercaseEvenWhenTheUuidIsOtherwiseValid()
    {
        var result = OperationId.TryParse(OperationId.Create().ToString().ToUpperInvariant());

        AssertInvalid(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("01890f3c7c007a8c9e5245d95f19d8ee")]
    [DataRow("{01890f3c-7c00-7a8c-9e52-45d95f19d8ee}")]
    [DataRow("550e8400-e29b-41d4-a716-446655440000")]
    [DataRow("01890f3c-7c00-7a8c-0e52-45d95f19d8ee")]
    [DataRow("01890f3c-7c00-7a8c-9e52-45d95f19d8eg")]
    [DataRow("01890f3c-7c00-7a8c-9e52-45d95f19d8ee ")]
    public void ParseRejectsNonCanonicalOrNonUuidVersionSevenText(string value)
    {
        var result = OperationId.TryParse(value);

        AssertInvalid(result);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void TypeCannotBeConstructedOrMutatedIntoAnInvalidState()
    {
        var type = typeof(OperationId);

        Assert.IsTrue(type.IsSealed);
        Assert.IsFalse(type.IsValueType);
        Assert.IsEmpty(type.GetConstructors());
        Assert.IsTrue(
            type.GetFields(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
                .All(field => field.IsInitOnly));
    }

    private static void AssertInvalid(Result<OperationId> result)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(StoreErrorCodes.InvalidOperationId, result.Error.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error.Kind);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
    }
}
