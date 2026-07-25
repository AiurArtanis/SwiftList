using SwiftList.Core.Services.Plugin.DirectoryIndex;

namespace SwiftList.Core.Tests.Services.Plugin.DirectoryIndex;

[TestClass]
public sealed class FilterPatternHelperTests
{
    [TestMethod]
    public void Split_EmptyOrWhitespace_DefaultsToMatchAll()
    {
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split(""));
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split("   "));
    }

    [TestMethod]
    public void Split_SinglePattern_ReturnsThatPattern()
    {
        CollectionAssert.AreEqual(new[] { "*.lnk" }, FilterPatternHelper.Split("*.lnk"));
    }

    [TestMethod]
    public void Split_MixedSeparatorsAndWhitespace_TrimsEachEntry()
    {
        CollectionAssert.AreEqual(new[] { "*.exe", "*.lnk", "*.bat" }, FilterPatternHelper.Split(" *.exe; *.lnk , *.bat "));
    }
}
