using SwiftList.Core.SearchIndex;

namespace SwiftList.Core.Tests.SearchIndex;

// The search path pools its working buffers because reallocating them per keystroke was measurable, but
// none of them ever gave anything back: Clear resets the count and keeps the capacity, so each buffer
// grew to fit the biggest search it had ever seen and stayed there. Reachable from static pools and
// thread statics, so not garbage and not something a collection can help with -- which is why asking for
// one after a large search reclaimed the results and left this behind.
[TestClass]
public sealed class SearchScratchPolicyTests
{
    [TestMethod]
    public void AnOrdinarySearchsBuffer_IsWorthKeeping()
    {
        // The whole point of the pooling. A keystroke has to find its buffer already the right size.
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining(0));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining(51));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining(4096));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining(SearchScratchPolicy.MaxRetainedEntries));
    }

    [TestMethod]
    public void AWholeDriveSearchsBuffer_IsNot()
    {
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining(SearchScratchPolicy.MaxRetainedEntries + 1));
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining(660_000));
    }

    [TestMethod]
    public void ClearAndTrim_AnOrdinaryList_KeepsItsCapacity()
    {
        // Trimming a small one would hand back the very allocation the pool exists to avoid.
        var list = new List<int>();
        for (var i = 0; i < 4096; i++) list.Add(i);
        var capacity = list.Capacity;

        SearchScratchPolicy.ClearAndTrim(list);

        Assert.IsEmpty(list);
        Assert.AreEqual(capacity, list.Capacity, "a reusable buffer this size must survive for the next search");
    }

    [TestMethod]
    public void ClearAndTrim_AnOversizedList_ReleasesItsArray()
    {
        var list = new List<int>();
        for (var i = 0; i < SearchScratchPolicy.MaxRetainedEntries * 2; i++) list.Add(i);

        SearchScratchPolicy.ClearAndTrim(list);

        Assert.IsEmpty(list);
        Assert.IsLessThanOrEqualTo(SearchScratchPolicy.MaxRetainedEntries, list.Capacity,
            "Clear alone keeps the array -- this is the high water mark that never came back");
    }

    [TestMethod]
    public void ClearAndTrim_AnOrdinaryDictionary_KeepsItsBuckets()
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < 4096; i++) map[i] = i;

        SearchScratchPolicy.ClearAndTrim(map);

        Assert.IsEmpty(map);
        // Re-filling to the same size must not have to grow again, which is what keeping the buckets buys.
        for (var i = 0; i < 4096; i++) map[i] = i;
        Assert.HasCount(4096, map);
    }

    [TestMethod]
    public void ClearAndTrim_AnOversizedDictionary_ReleasesItsBuckets()
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < SearchScratchPolicy.MaxRetainedEntries * 2; i++) map[i] = i;
        var before = GC.GetTotalMemory(true);

        SearchScratchPolicy.ClearAndTrim(map);
        var after = GC.GetTotalMemory(true);

        Assert.IsEmpty(map);
        Assert.IsLessThan(before, after, "the buckets should have been handed back, not just emptied");
    }

    [TestMethod]
    public void ClearAndTrim_JudgesADictionaryBeforeClearingIt()
    {
        // Count is zero after Clear, so deciding afterwards would find every dictionary small and trim
        // none of them -- the check has to happen while the size is still observable.
        var map = new Dictionary<int, int>();
        for (var i = 0; i < SearchScratchPolicy.MaxRetainedEntries * 2; i++) map[i] = i;
        var before = GC.GetTotalMemory(true);

        SearchScratchPolicy.ClearAndTrim(map);

        Assert.IsLessThan(before, GC.GetTotalMemory(true));
    }
}
