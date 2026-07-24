using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfPatternTests
{
    [TestMethod]
    public void Parse_EmptyQuery_IsEmpty() => Assert.IsTrue(FzfPattern.Parse("").IsEmpty);

    [TestMethod]
    public void Parse_DriveLetterTerm_ExtractsTargetDriveAndDropsItFromTerms()
    {
        var pattern = FzfPattern.Parse("c: readme");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_MatchesSubsequence()
    {
        var pattern = FzfPattern.Parse("rdm");

        var matched = pattern.TryMatch("readme.md", out var result, FzfScoringScheme.Default);

        Assert.IsTrue(matched);
        Assert.IsTrue(result.ValidOffsetFound);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_FailsWhenSubsequenceAbsent()
    {
        var pattern = FzfPattern.Parse("xyz");

        var matched = pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default);

        Assert.IsFalse(matched);
    }

    [TestMethod]
    public void TryMatch_MultipleTerms_RequiresEveryTermToMatch()
    {
        var pattern = FzfPattern.Parse("read md");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_InverseTerm_RejectsTextContainingIt()
    {
        var pattern = FzfPattern.Parse("read !md");

        Assert.IsTrue(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_PrefixTerm_OnlyMatchesAtStart()
    {
        var pattern = FzfPattern.Parse("^read");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("unread.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_SuffixTerm_OnlyMatchesAtEnd()
    {
        var pattern = FzfPattern.Parse("md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("md5sum.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_EqualTerm_RequiresExactWholeTextMatch()
    {
        var pattern = FzfPattern.Parse("^readme.md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md.bak", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_ExactBoundaryTerm_RequiresWholeSegmentMatch()
    {
        var pattern = FzfPattern.Parse("'read'");

        // "read" is its own dot-delimited segment in "my.read.txt" -- a boundary on both sides.
        Assert.IsTrue(pattern.TryMatch("my.read.txt", out _, FzfScoringScheme.Default));
        // In "readme.md" the match would end mid-word (right before "me"), which is not a boundary.
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        // Not even a contiguous substring here.
        Assert.IsFalse(pattern.TryMatch("r-e-a-d.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_MixedCaseTerm_IsCaseSensitive()
    {
        var pattern = FzfPattern.Parse("README");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_LowercaseTerm_IsCaseInsensitive()
    {
        var pattern = FzfPattern.Parse("readme");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_EscapedSpace_IsTreatedAsLiteralSpaceInOneTerm()
    {
        var pattern = FzfPattern.Parse(@"my\ file");

        Assert.IsTrue(pattern.TryMatch("my file.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("myfile.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_MatchesIfEitherSegmentMatches()
    {
        var pattern = FzfPattern.Parse("he");

        // "he" and "hu" are alternate readings of the same alias, joined with '|' at the text side.
        Assert.IsTrue(pattern.TryMatch("he|hu|huo", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_TermsFromDifferentSegmentsDoNotCombine()
    {
        var pattern = FzfPattern.Parse("ab cd");

        // "ab" only appears in the first segment and "cd" only in the second -- a match must find
        // both terms within the SAME segment, not scattered across the whole joined string.
        Assert.IsFalse(pattern.TryMatch("ab|cd", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("abcd|xy", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void GetTotalTermLength_SumsPositiveTermsOnlyExcludingInverse()
    {
        var pattern = FzfPattern.Parse("read !md");

        Assert.AreEqual("read".Length, pattern.GetTotalTermLength());
    }
}
