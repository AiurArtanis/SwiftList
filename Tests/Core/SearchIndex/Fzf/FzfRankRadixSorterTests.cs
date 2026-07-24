using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfRankRadixSorterTests
{
    [TestMethod]
    public void Sort_SmallList_OrdersAscendingBySortKey()
    {
        var ranks = new List<FzfRank>
        {
            new(0, 0, 300),
            new(1, 0, 100),
            new(2, 0, 200),
        };

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(new ulong[] { 100, 200, 300 }, ranks.ConvertAll(r => r.SortKey));
    }

    [TestMethod]
    public void Sort_SmallListWithEqualKeys_TieBreaksByEntryIndexAscending()
    {
        var ranks = new List<FzfRank>
        {
            new(EntryIndex: 5, Score: 0, SortKey: 10),
            new(EntryIndex: 2, Score: 0, SortKey: 10),
            new(EntryIndex: 8, Score: 0, SortKey: 10),
        };

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(new[] { 2, 5, 8 }, ranks.ConvertAll(r => r.EntryIndex));
    }

    [TestMethod]
    public void Sort_LargeList_UsesRadixPathAndOrdersAscendingBySortKey()
    {
        // >=128 entries forces the radix-sort branch instead of List.Sort.
        var random = new Random(Seed: 42);
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 500; i++)
            ranks.Add(new FzfRank(i, 0, (ulong)random.NextInt64(0, 1_000_000)));

        FzfRankRadixSorter.Sort(ranks);

        var keys = ranks.ConvertAll(r => r.SortKey);
        var sortedKeys = new List<ulong>(keys);
        sortedKeys.Sort();
        CollectionAssert.AreEqual(sortedKeys, keys);
    }

    [TestMethod]
    public void Sort_LargeListWithDuplicateKeys_TieBreaksByEntryIndexAscending()
    {
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 200; i++)
            ranks.Add(new FzfRank(EntryIndex: 199 - i, Score: 0, SortKey: 42)); // all same key, reverse entry-index order

        FzfRankRadixSorter.Sort(ranks);

        for (var i = 0; i < ranks.Count; i++)
            Assert.AreEqual(i, ranks[i].EntryIndex);
    }

    [TestMethod]
    public void Sort_EmptyList_DoesNotThrow()
    {
        var ranks = new List<FzfRank>();

        FzfRankRadixSorter.Sort(ranks);

        Assert.IsEmpty(ranks);
    }
}
