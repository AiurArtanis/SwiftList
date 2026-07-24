using SwiftList.Core.SearchIndex.Query;

namespace SwiftList.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class SearchQueryParserTests
{
    [TestMethod]
    public void Parse_PlainKeyword_IsNotPathMode()
    {
        var result = SearchQueryParser.Parse("readme");

        Assert.IsFalse(result.IsPathMode);
        Assert.IsNull(result.TargetDrive);
    }

    [TestMethod]
    public void Parse_DriveLetterTerm_SetsTargetDriveWithoutPathMode()
    {
        var result = SearchQueryParser.Parse("c: readme");

        Assert.IsFalse(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
    }

    [TestMethod]
    public void Parse_DrivePath_IsPathModeWithNormalizedDrive()
    {
        var result = SearchQueryParser.Parse(@"c:\foo\bar");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
        Assert.IsFalse(result.PathEndsWithSeparator);
    }

    [TestMethod]
    public void Parse_DrivePathWithTrailingSeparator_SetsPathEndsWithSeparator()
    {
        var result = SearchQueryParser.Parse(@"c:\foo\bar\");

        Assert.IsTrue(result.PathEndsWithSeparator);
        Assert.AreEqual(@"c:\foo\bar", result.ExactPathLower);
    }

    [TestMethod]
    public void Parse_ForwardSlashes_AreNormalizedToBackslashes()
    {
        var result = SearchQueryParser.Parse("c:/foo/bar");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_DriveRootShorthand_NormalizesToVolumeSeparatorForm()
    {
        // "c\foo" (drive letter immediately followed by a separator, no colon) is treated the same
        // as "c:\foo" -- see SearchQueryParser.TryNormalizeDrivePath.
        var result = SearchQueryParser.Parse(@"c\foo");

        Assert.IsTrue(result.IsPathMode);
        Assert.AreEqual("c", result.TargetDrive);
        Assert.AreEqual(@"c:\foo", result.PathPatternLower);
    }

    [TestMethod]
    public void Parse_MixedCaseQuery_IsLowercased()
    {
        var result = SearchQueryParser.Parse(@"C:\Foo\BAR");

        Assert.AreEqual(@"c:\foo\bar", result.PathPatternLower);
    }

    [TestMethod]
    [DataRow(@"c:\foo\bar", @"c:\foo\bar")]
    [DataRow(@"c:\foo\bar\", @"c:\foo\bar")]
    [DataRow(@"c:\foo\bar\\\", @"c:\foo\bar")]
    [DataRow(@"c:\", @"c:\")]
    public void NormalizeExactPath_TrimsTrailingSeparators(string input, string expected) => Assert.AreEqual(expected, SearchQueryParser.NormalizeExactPath(input));
}
