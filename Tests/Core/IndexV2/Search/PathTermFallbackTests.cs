using SwiftList.Core.IndexV2.Search;

namespace SwiftList.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class PathTermFallbackTests
{
    // A layout where many files share one name and are told apart only by the folder above them --
    // the shape the ancestor pass exists for. Names here are generic on purpose (repo rule 15).
    private static LiveIndexFixture BuildSeriesDrive() => LiveIndexFixture.Build("T", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "library", FileRecordFlags.Directory),
        new FileRecord(3, 2, "drama", FileRecordFlags.Directory),
        new FileRecord(4, 3, "Rose Season", FileRecordFlags.Directory),
        new FileRecord(5, 4, "Episode01.mp4", FileRecordFlags.None),
        new FileRecord(6, 3, "Tulip Season", FileRecordFlags.Directory),
        new FileRecord(7, 6, "Episode01.mp4", FileRecordFlags.None),
        new FileRecord(8, 3, "Rose Notes.txt", FileRecordFlags.None),
    });

    private static List<SearchResult> Search(LiveIndexFixture fixture, string query, int limit = 10)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None);
        return results;
    }

    [TestMethod]
    public void SearchStreaming_TermMatchingOnlyAnAncestorFolder_StillMatches()
    {
        using var fixture = BuildSeriesDrive();

        // "episode01" hits the file name, "tulip" only the series folder above it.
        var results = Search(fixture, "episode01 tulip");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Tulip Season\Episode01.mp4", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_AncestorTermsAreOrderFree()
    {
        using var fixture = BuildSeriesDrive();

        // Same two terms typed the other way round: unlike path mode, no positional meaning.
        var results = Search(fixture, "tulip episode01");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Tulip Season\Episode01.mp4", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_TermSatisfiedByAGrandparent_StillMatches()
    {
        using var fixture = BuildSeriesDrive();

        // "library" is two levels above the file, so this only works if the whole chain is walked.
        var results = Search(fixture, "episode01 library");

        Assert.HasCount(2, results);
        CollectionAssert.AreEquivalent(
            new[] { @"T:\library\drama\Rose Season\Episode01.mp4", @"T:\library\drama\Tulip Season\Episode01.mp4" },
            results.Select(r => r.Path).ToList());
    }

    [TestMethod]
    public void SearchStreaming_NameOnlyMatchWins_FallbackNeverRuns()
    {
        using var fixture = BuildSeriesDrive();

        // Both terms match one file name outright, so the ordinary name search satisfies the query and
        // the ancestor pass must not add the episode files sitting under a folder called "Rose".
        var results = Search(fixture, "rose notes");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Rose Notes.txt", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_FolderOnlyTerms_MatchTheFolderWithoutDumpingItsContents()
    {
        using var fixture = BuildSeriesDrive();

        var results = Search(fixture, "library drama");

        // Directories are indexed rows with names of their own, so "drama" (own name) plus "library"
        // (its parent) legitimately identifies the folder itself.
        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama", results[0].Path);
        Assert.IsTrue(results[0].IsDir);
        // The point of requiring one term to hit a name: the episodes underneath match neither term,
        // so describing only their folders never dumps the whole subtree.
        Assert.IsFalse(results.Any(r => r.Name.Contains("Episode", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SearchStreaming_SingleTermQuery_IsUnaffected()
    {
        using var fixture = BuildSeriesDrive();

        // One term has nowhere to split, so a folder-only word still finds only the folder itself.
        var results = Search(fixture, "tulip");

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].IsDir);
    }

    [TestMethod]
    public void SearchStreaming_UnmatchableTerm_StillReturnsNothing()
    {
        using var fixture = BuildSeriesDrive();

        var results = Search(fixture, "episode01 nosuchfolder");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilterStillApplies()
    {
        using var fixture = BuildSeriesDrive();

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "episode01 library", 10, results.Add,
            CancellationToken.None, directoryFilter: @"T:\library\drama\Rose Season");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Rose Season\Episode01.mp4", results[0].Path);
    }
}
