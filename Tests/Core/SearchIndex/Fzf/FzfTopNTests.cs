using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfTopNTests
{
    // Smaller SortKey means a better-ranked candidate (see FzfResultRank.ScorePoint's inversion) --
    // FzfTopN retains the entries with the SMALLEST sort keys up to capacity.
    [TestMethod]
    public void Add_WithinCapacity_RetainsAllEntries()
    {
        var topN = new FzfTopN(3);
        topN.Add(new FzfRank(0, 10, 100));
        topN.Add(new FzfRank(1, 20, 50));

        Assert.AreEqual(2, topN.Count);
    }

    [TestMethod]
    public void Add_BeyondCapacity_EvictsTheWorstSortKey()
    {
        var topN = new FzfTopN(2);
        topN.Add(new FzfRank(0, 0, 30));
        topN.Add(new FzfRank(1, 0, 10));
        topN.Add(new FzfRank(2, 0, 20)); // should evict entry 0 (SortKey 30, the worst of the three)

        var finished = topN.Finish(10);

        Assert.HasCount(2, finished);
        CollectionAssert.DoesNotContain(finished.ConvertAll(r => r.EntryIndex), 0);
    }

    [TestMethod]
    public void Add_BetterThanCurrentWorst_ReplacesIt()
    {
        var topN = new FzfTopN(1);
        topN.Add(new FzfRank(0, 0, 100));
        topN.Add(new FzfRank(1, 0, 1)); // strictly smaller SortKey -- should replace entry 0

        var finished = topN.Finish(10);

        Assert.HasCount(1, finished);
        Assert.AreEqual(1, finished[0].EntryIndex);
    }

    [TestMethod]
    public void Add_WorseThanCurrentWorst_IsDropped()
    {
        var topN = new FzfTopN(1);
        topN.Add(new FzfRank(0, 0, 1));
        topN.Add(new FzfRank(1, 0, 100)); // larger SortKey -- should be dropped

        var finished = topN.Finish(10);

        Assert.HasCount(1, finished);
        Assert.AreEqual(0, finished[0].EntryIndex);
    }

    [TestMethod]
    public void Finish_ReturnsEntriesSortedBySortKeyAscending()
    {
        var topN = new FzfTopN(5);
        topN.Add(new FzfRank(0, 0, 300));
        topN.Add(new FzfRank(1, 0, 100));
        topN.Add(new FzfRank(2, 0, 200));

        var finished = topN.Finish(10);

        CollectionAssert.AreEqual(new[] { 1, 2, 0 }, finished.ConvertAll(r => r.EntryIndex));
    }

    [TestMethod]
    public void Finish_LimitSmallerThanCount_TruncatesToLimit()
    {
        var topN = new FzfTopN(5);
        for (var i = 0; i < 5; i++)
            topN.Add(new FzfRank(i, 0, (ulong)(50 - i)));

        var finished = topN.Finish(2);

        Assert.HasCount(2, finished);
    }

    [TestMethod]
    public void Reset_ClearsPreviouslyAddedEntries()
    {
        var topN = new FzfTopN(2);
        topN.Add(new FzfRank(0, 0, 10));
        topN.Reset();

        Assert.AreEqual(0, topN.Count);
        topN.Add(new FzfRank(1, 0, 5));
        Assert.AreEqual(1, topN.Count);
    }

    [TestMethod]
    public void DrainInto_MergesEntriesRespectingTargetCapacity()
    {
        var worker = new FzfTopN(2);
        worker.Add(new FzfRank(0, 0, 5));
        worker.Add(new FzfRank(1, 0, 1));

        var merged = new FzfTopN(2);
        merged.Add(new FzfRank(2, 0, 3));

        worker.DrainInto(merged);
        var finished = merged.Finish(10);

        // merged started with {2:3}, worker drains in {0:5} and {1:1} -- best 2 of {3,5,1} are {1,3}.
        CollectionAssert.AreEqual(new[] { 1, 2 }, finished.ConvertAll(r => r.EntryIndex));
    }
}
