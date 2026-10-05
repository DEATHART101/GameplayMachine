using ODStudio.Model;

namespace ODStudio.Tests;

public sealed class TypePresentationTests
{
    [TestCase(OdBuiltInType.Boolean, "bool")]
    [TestCase(OdBuiltInType.Int32, "int")]
    [TestCase(OdBuiltInType.Int64, "long")]
    [TestCase(OdBuiltInType.Single, "float")]
    [TestCase(OdBuiltInType.String, "string")]
    public void BuiltInTypesUseCompactNames(OdBuiltInType type, string expected)
    {
        Assert.That(OdTypePresentation.BuiltInDisplayName(type), Is.EqualTo(expected));
    }

    [Test]
    public void FuzzySearchMatchesSubsequencesAndPrefersDirectMatches()
    {
        int direct = OdTypePresentation.FuzzyScore("PlayerState", "Class", "RPG", "player");
        int subsequence = OdTypePresentation.FuzzyScore("PlayerState", "Class", "RPG", "pstate");
        int missing = OdTypePresentation.FuzzyScore("Inventory", "Class", "RPG", "xyz");

        Assert.Multiple(() =>
        {
            Assert.That(direct, Is.GreaterThan(subsequence));
            Assert.That(subsequence, Is.GreaterThanOrEqualTo(0));
            Assert.That(missing, Is.EqualTo(-1));
        });
    }
}
