using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Domain.Tests;

[TestClass]
public sealed class ConstructionSafetyTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void DomainValuesExposeNoPublicConstructionOrMutation()
    {
        var domainValueTypes = new[]
        {
            typeof(SessionIdentity),
            typeof(IncidentId),
            typeof(SessionNumber),
            typeof(SessionTime),
            typeof(ReplayPosition),
            typeof(UtcInstant),
            typeof(IncidentCounter),
            typeof(LapNumber),
            typeof(LapDistance),
        };

        foreach (var type in domainValueTypes)
        {
            Assert.HasCount(0, type.GetConstructors(), $"{type.Name} exposes a public constructor.");
            Assert.IsTrue(
                type.GetProperties().All(static property => property.SetMethod is null),
                $"{type.Name} exposes a mutable property.");
            Assert.IsTrue(type.IsSealed, $"{type.Name} must remain sealed.");
        }
    }
}
