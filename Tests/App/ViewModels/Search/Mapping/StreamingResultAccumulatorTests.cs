using SwiftList.App.ViewModels.Search.Mapping;
using SwiftList.Core;

namespace SwiftList.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class StreamingResultAccumulatorTests
{
    private static readonly Dictionary<string, int> NoHistory = new();

    // With no history and no rank key set, SearchResultRankComparer falls through to path LENGTH before
    // the path itself -- so paths of differing length give a ranked order that is deliberately not the
    // arrival order, which is what makes the ordering assertions below mean something.
    private static SearchResult Result(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        Path = path,
        IsDir = false,
        Drive = "D",
    };

    private static List<SearchResult> Arrivals(params string[] paths) => paths.Select(Result).ToList();

    private static List<string> Paths(IEnumerable<SwiftList.App.AppSearchResult> rows) =>
        rows.Select(r => r.FullPath).ToList();

    [TestMethod]
    public void Absorb_RanksResultsRatherThanKeepingArrivalOrder()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);

        var rows = accumulator.Absorb(Arrivals(@"D:\aaaaaa", @"D:\a", @"D:\aaa"));

        CollectionAssert.AreEqual(new[] { @"D:\a", @"D:\aaa", @"D:\aaaaaa" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_InChunks_MatchesAbsorbingEverythingAtOnce()
    {
        // The property the whole design rests on: painting progressively must produce exactly the list
        // that painting once at the end would have. A merge that got the order subtly wrong would show
        // up here and nowhere else, because every other symptom of it looks like plausible ranking.
        var paths = Enumerable.Range(0, 500).Select(i => @"D:\" + new string('x', 1 + i % 40) + i).ToArray();

        var oneShot = new StreamingResultAccumulator("x", NoHistory);
        var expected = Paths(oneShot.Absorb(Arrivals(paths)));

        var streamed = new StreamingResultAccumulator("x", NoHistory);
        var growing = new List<SearchResult>();
        List<string> actual = new();
        foreach (var chunk in new[] { 1, 7, 3, 60, 200, 229 })
        {
            growing.AddRange(paths.Skip(growing.Count).Take(chunk).Select(Result));
            actual = Paths(streamed.Absorb(growing));
        }

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Absorb_ALaterArrivalThatOutranksEverything_LandsAtTheTop()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aaaa", @"D:\aaaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\a"));
        var rows = accumulator.Absorb(growing);

        CollectionAssert.AreEqual(new[] { @"D:\a", @"D:\aaaa", @"D:\aaaaa" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_BuildsEachRowExactlyOnce()
    {
        // The reason progressive painting is affordable at all. If a later paint rebuilt earlier rows,
        // the cost would be quadratic in the number of paints and we would be back to rationing them.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aa", @"D:\aaa");
        var first = accumulator.Absorb(growing);
        var originals = first.ToList();

        growing.Add(Result(@"D:\aaaa"));
        var second = accumulator.Absorb(growing);

        Assert.AreSame(originals[0], second[0]);
        Assert.AreSame(originals[1], second[1]);
    }

    [TestMethod]
    public void Absorb_TheSameArrivalsTwice_AddsNothing()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aa", @"D:\aaa");
        accumulator.Absorb(growing);

        var rows = accumulator.Absorb(growing);

        Assert.HasCount(2, rows);
        Assert.AreEqual(2, accumulator.Consumed);
    }

    [TestMethod]
    public void Absorb_StampsRowIndexesInRankOrder()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);

        var rows = accumulator.Absorb(Arrivals(@"D:\aaaaaa", @"D:\a", @"D:\aaa"));

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Index).ToList());
    }

    [TestMethod]
    public void Absorb_RestampsIndexesAfterALaterArrivalReordersTheList()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aaaa", @"D:\aaaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\a"));
        var rows = accumulator.Absorb(growing);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Index).ToList());
    }

    [TestMethod]
    public void Absorb_NoArrivals_ReturnsAnEmptyList()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);

        Assert.IsEmpty(accumulator.Absorb(new List<SearchResult>()));
    }

    [TestMethod]
    public void Absorb_QueryIsADirectoryPath_DropsThatDirectoryItself()
    {
        // Typing an exact directory is a request to look inside it, so its own index record is not one
        // of its own results. Applied per arrival here rather than as a RemoveAll over the whole
        // snapshot, since the accumulator only ever sees each arrival once.
        var root = @"D:\";
        var accumulator = new StreamingResultAccumulator(root, NoHistory);

        var rows = accumulator.Absorb(Arrivals(root, @"D:\keep-me"));

        CollectionAssert.AreEqual(new[] { @"D:\keep-me" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_HistoryPriority_OutranksEverythingElse()
    {
        var history = new Dictionary<string, int> { [@"D:\zzzzzzzzzz"] = 0 };
        var accumulator = new StreamingResultAccumulator("z", history);

        var rows = accumulator.Absorb(Arrivals(@"D:\a", @"D:\zzzzzzzzzz"));

        CollectionAssert.AreEqual(new[] { @"D:\zzzzzzzzzz", @"D:\a" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_ManyChunks_KeepsTheListFullyOrdered()
    {
        var accumulator = new StreamingResultAccumulator("f", NoHistory);
        var growing = new List<SearchResult>();
        var rnd = 7;
        for (var round = 0; round < 40; round++)
        {
            for (var i = 0; i < 25; i++)
            {
                rnd = rnd * 1103515245 + 12345;
                growing.Add(Result(@"D:\" + new string('f', 1 + Math.Abs(rnd % 60)) + growing.Count));
            }
            accumulator.Absorb(growing);
        }

        var rows = accumulator.Absorb(growing);
        var lengths = rows.Select(r => r.FullPath.Length).ToList();
        for (var i = 1; i < lengths.Count; i++)
            Assert.IsLessThanOrEqualTo(lengths[i], lengths[i - 1], $"row {i} is out of rank order");
    }
}
