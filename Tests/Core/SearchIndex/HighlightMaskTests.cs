using SwiftList.Core.SearchIndex;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Tests.SearchIndex;

[TestClass]
public sealed class HighlightMaskTests
{
    [TestMethod]
    public void Compute_EmptyText_ReturnsEmptyMask()
    {
        var mask = HighlightMask.Compute("", FzfPattern.Parse("read"));

        Assert.IsEmpty(mask);
    }

    [TestMethod]
    public void Compute_LiteralSubstring_MarksEveryOccurrence()
    {
        // MarkLiteralSpan finds every occurrence of a term, not just the first.
        var mask = HighlightMask.Compute("ababab", FzfPattern.Parse("ab"));

        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, mask);
    }

    [TestMethod]
    public void Compute_ScatteredFuzzyMatch_MarksOnlyMatchedCharacters()
    {
        // "chwx" against "china_white_x" has no literal substring -- falls through to the direct
        // fuzzy backtrace (FzfPositionMatcher), which should mark exactly the matched subsequence.
        var mask = HighlightMask.Compute("cwx", FzfPattern.Parse("cwx"));

        Assert.IsTrue(Array.TrueForAll(mask, m => m));
    }

    [TestMethod]
    public void Compute_NoMatch_ReturnsAllFalseMask()
    {
        var mask = HighlightMask.Compute("readme", FzfPattern.Parse("xyz"));

        Assert.IsFalse(Array.Exists(mask, m => m));
    }

    [TestMethod]
    public void ComputeWeight_EmptyText_ReturnsZero() => Assert.AreEqual(0, HighlightMask.ComputeWeight("", FzfPattern.Parse("read")));

    [TestMethod]
    public void ComputeWeight_FullMatch_ReturnsOne() => Assert.AreEqual(1.0, HighlightMask.ComputeWeight("read", FzfPattern.Parse("read")));

    [TestMethod]
    public void ComputeWeight_NoMatch_ReturnsZero() => Assert.AreEqual(0, HighlightMask.ComputeWeight("readme", FzfPattern.Parse("xyz")));

    [TestMethod]
    public void ComputeWeight_ContiguousMatch_ScoresHigherThanScattered()
    {
        var contiguous = HighlightMask.ComputeWeight("abcdef", FzfPattern.Parse("abc"));
        var scattered = HighlightMask.ComputeWeight("axbxcx", FzfPattern.Parse("abc"));

        Assert.IsGreaterThan(scattered, contiguous);
    }
}
