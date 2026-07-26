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

    // Regression coverage: Mark used to always highlight the OR set's FIRST term regardless of whether
    // it actually matched this candidate, mirroring FzfPattern.TryMatchSingle's per-set "best matching
    // term" semantics only in its own comment, not its code. A candidate that only matched via the
    // second or third OR term came back with an all-false mask -- no highlight at all -- even though the
    // real match algorithm matched it correctly via that later term.
    [TestMethod]
    public void Compute_OrQuery_HighlightsWhicheverTermActuallyMatchedTheCandidate()
    {
        var pattern = FzfPattern.Parse("123 | 456 | 789");

        var maskForFirstTerm = HighlightMask.Compute("123", pattern);
        var maskForSecondTerm = HighlightMask.Compute("456", pattern);
        var maskForThirdTerm = HighlightMask.Compute("789", pattern);

        Assert.IsTrue(Array.TrueForAll(maskForFirstTerm, m => m));
        Assert.IsTrue(Array.TrueForAll(maskForSecondTerm, m => m));
        Assert.IsTrue(Array.TrueForAll(maskForThirdTerm, m => m));
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
