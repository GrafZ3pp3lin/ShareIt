using kroet.ShareIt.Api.Content;

namespace kroet.ShareIt.Api.Test.Content;

[TestClass]
public class ShortCodeTests
{
    [TestMethod]
    public void Alphabet_HasExactlyThirtyOneUnambiguousCharacters()
    {
        Assert.AreEqual(31, ShortCode.Alphabet.Length);
        Assert.AreEqual(31, ShortCode.Alphabet.Distinct().Count());
        foreach (var excluded in "01IOL")
        {
            Assert.IsFalse(ShortCode.Alphabet.Contains(excluded, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Locator_IsThreeCharacters()
    {
        Assert.AreEqual(3, ShortCode.Length);
        Assert.AreEqual(5, ShortCode.MaxFailedUnlockAttempts);
        Assert.AreEqual(TimeSpan.FromMinutes(10), ShortCode.PackageLifetime);
    }

    [TestMethod]
    public void Normalize_TrimsSeparatorsAndUppercases()
    {
        Assert.AreEqual("ABC", ShortCode.Normalize("  a-b-c "));
        Assert.AreEqual("ABC", ShortCode.Normalize("A B C"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("AB")]
    [DataRow("ABCD")]
    [DataRow("ABO")]
    [DataRow("ABI")]
    [DataRow("ABL")]
    [DataRow("AB!")]
    [DataRow("Aſ")]
    [DataRow("ß")]
    public void Normalize_RejectsInvalidValues(string? value)
    {
        Assert.IsNull(ShortCode.Normalize(value));
    }

    [TestMethod]
    public void Normalize_RejectsOverlongInputBeforeAllocatingTheBuffer()
    {
        Assert.IsNull(ShortCode.Normalize(new string('A', 65)));
    }

    [TestMethod]
    public void Generator_ProducesThreeCharactersFromTheAlphabet()
    {
        var generator = new ShortCodeGenerator();

        for (var index = 0; index < 1_000; index++)
        {
            var code = generator.Next();

            Assert.AreEqual(ShortCode.Length, code.Length);
            Assert.IsTrue(ShortCode.Normalize(code) is not null, code);
        }
    }
}
