namespace SwiftList.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinAliasCombinationGeneratorTests
{
    [TestMethod]
    public void GenerateAliases_SingleChineseChar_ReturnsPinyinsDirectly()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中");

        CollectionAssert.Contains(aliases, "zhong");
    }

    [TestMethod]
    public void GenerateAliases_TwoCharMonophonicWord_ReturnsInitialsAndFull()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中国");

        CollectionAssert.Contains(aliases, "zg"); // initials
        CollectionAssert.Contains(aliases, "zhongguo"); // full pinyin
    }

    [TestMethod]
    public void GenerateAliases_EveryAlias_IsLowercaseAscii()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中国人");

        foreach (var alias in aliases)
        {
            foreach (var part in alias.Split('|'))
            {
                Assert.IsTrue(part.Length == 0 || System.Text.Ascii.IsValid(part));
                Assert.AreEqual(part.ToLowerInvariant(), part);
            }
        }
    }

    [TestMethod]
    public void GenerateAliases_MixedChineseAndAscii_KeepsAsciiCharsLiteralAndLowercased()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中ABC");

        CollectionAssert.Contains(aliases, "zabc");
    }

    [TestMethod]
    public void GenerateAliases_NonChineseNonAsciiChar_LowercasedLiterally()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中É");

        Assert.IsTrue(aliases.Any(a => a.EndsWith('é')));
    }

    [TestMethod]
    public void GetSyllableLists_ChineseChar_ReturnsPinyinCandidates()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("中");

        CollectionAssert.Contains(lists[0], "zhong");
    }

    [TestMethod]
    public void GetSyllableLists_AsciiChar_ReturnsLowercasedSingleCharList()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("A");

        CollectionAssert.AreEqual(new[] { "a" }, lists[0]);
    }
}
