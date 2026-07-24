using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinAliasProviderTests
{
    private static readonly PinyinAliasProvider Provider = new();

    [TestMethod]
    public void CanHandle_ContainsChinese_ReturnsTrue() => Assert.IsTrue(Provider.CanHandle("hello 中文"));

    [TestMethod]
    public void CanHandle_PureAscii_ReturnsFalse() => Assert.IsFalse(Provider.CanHandle("hello world"));

    [TestMethod]
    public void CanHandle_EmptyOrNull_ReturnsFalse()
    {
        Assert.IsFalse(Provider.CanHandle(""));
        Assert.IsFalse(Provider.CanHandle(null!));
    }

    [TestMethod]
    public void GetAliases_ChineseText_ReturnsNonEmptyResults()
    {
        var aliases = Provider.GetAliases("中国").ToList();

        Assert.IsNotEmpty(aliases);
    }

    [TestMethod]
    public void GetAliases_CalledTwiceWithSameText_ReturnsCachedSameReference()
    {
        var first = Provider.GetAliases("中国人民");
        var second = Provider.GetAliases("中国人民");

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetAliases_EmptyText_ReturnsEmpty() => Assert.IsEmpty(Provider.GetAliases(""));

    [TestMethod]
    public void MapAliasToSourceIndices_InitialsAlias_ReturnsIdentityMapping()
    {
        var map = Provider.MapAliasToSourceIndices("中国", "zg");

        Assert.IsNotNull(map);
        CollectionAssert.AreEqual(new[] { 0, 1 }, map);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_FullPinyinAlias_MapsEachSyllableToItsSourceChar()
    {
        var map = Provider.MapAliasToSourceIndices("中国", "zhongguo");

        Assert.IsNotNull(map);
        // "zhong" (5 letters) -> source char 0, "guo" (3 letters) -> source char 1
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 1, 1, 1 }, map);
    }

    [TestMethod]
    public void MapAliasToSourceIndices_EmptyInputs_ReturnsNull()
    {
        Assert.IsNull(Provider.MapAliasToSourceIndices("", "zg"));
        Assert.IsNull(Provider.MapAliasToSourceIndices("中国", ""));
    }

    [TestMethod]
    public void MapAliasToSourceIndices_UnmatchableAlias_ReturnsNull() => Assert.IsNull(Provider.MapAliasToSourceIndices("中国", "xyz123"));

    [TestMethod]
    public void GetAliasesUtf8_MatchesGetAliases()
    {
        var text = "中国人民";
        var expected = Provider.GetAliases(text).ToList();

        var sink = new AliasByteSink();
        Provider.GetAliasesUtf8(text, sink);
        var decoded = new List<string>(sink.SegmentCount);
        for (var i = 0; i < sink.SegmentCount; i++)
            decoded.Add(Encoding.UTF8.GetString(sink.Segment(i)));

        CollectionAssert.AreEquivalent(expected, decoded);
    }
}
