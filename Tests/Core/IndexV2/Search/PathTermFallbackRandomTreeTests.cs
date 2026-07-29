using SwiftList.Core.IndexV2.Search;
using SwiftList.Core.SearchIndex;

namespace SwiftList.Core.Tests.IndexV2.Search;

// Checks the ancestor pass against an independent statement of what it is supposed to return, over
// randomly shaped trees.
//
// It exists because the pass memoises its answer for a folder and shares it with every folder below,
// which is a real optimisation over a real property (the answer is a union along the chain, and each
// folder's chain is a suffix of its children's) -- but a sharing bug there does not fail loudly. It
// hides results: a file that should have matched is silently dropped, and only for queries that reach
// this pass at all, which is the subset a user is least likely to notice or report.
//
// The reference below is written from the rule, not from the implementation. Deriving it by turning the
// memo off would share every helper with the thing under test, so any mistake inside those helpers
// would agree with itself and pass.
[TestClass]
public sealed class PathTermFallbackRandomTreeTests
{
    // Short, overlapping, ASCII only. Overlap is the point -- terms have to be satisfiable by several
    // different segments so that sharing an answer has something to get wrong. ASCII keeps the alias
    // tier out of it, which the reference does not model.
    private static readonly string[] Words =
    {
        "alpha", "alpine", "album", "beta", "berry", "bench", "gamma", "gamut",
        "delta", "delve", "omega", "omen", "sigma", "signal", "theta", "there",
    };

    private static readonly string[] Extensions = { ".txt", ".log", ".dat", ".cfg" };

    private sealed record Row(UInt128 Id, UInt128 ParentId, string Name, bool IsDirectory, string FullPath);

    [TestMethod]
    public void EveryRandomTree_ReturnsExactlyWhatTheRuleSaysItShould()
    {
        // A comparison that finds nothing on both sides passes while testing nothing. These count what
        // was actually put in front of the pass, and are asserted at the end.
        var queriesRun = 0;
        var rowsExpected = 0;
        var rowsNeedingAnAncestor = 0;

        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            var rows = BuildTree(random);
            using var fixture = LiveIndexFixture.Build("T", rows.Select(r =>
                new FileRecord(r.Id, r.ParentId, r.Name,
                    r.IsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None)).Prepend(LiveIndexFixture.Root()));

            foreach (var terms in QueriesFor(random))
            {
                var query = string.Join(' ', terms);
                var actual = Search(fixture, query);
                var expected = Expected(rows, terms, out var viaAncestor);

                queriesRun++;
                rowsExpected += expected.Count;
                rowsNeedingAnAncestor += viaAncestor;

                CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList(),
                    $"seed {seed}, query \"{query}\"\n" +
                    $"missing: {string.Join(", ", expected.Except(actual))}\n" +
                    $"unexpected: {string.Join(", ", actual.Except(expected))}");
            }
        }

        Assert.IsGreaterThan(200, queriesRun, "not enough queries were generated to be worth anything");
        Assert.IsGreaterThan(200, rowsExpected, "the trees produced almost nothing to match");
        // The ones that only match because a FOLDER supplied a term -- the pass this exists to check.
        // Without them the whole run could be satisfied by plain name search.
        Assert.IsGreaterThan(100, rowsNeedingAnAncestor,
            "no result depended on an ancestor folder, so the ancestor pass was never exercised");
    }

    /// <summary>
    /// What the pass promises, stated directly: a row is returned when its own name satisfies at least
    /// one term, and its name together with the folders above it (and the drive root's own segments)
    /// satisfies all of them. Order does not matter -- unlike path mode, these terms carry no position.
    /// </summary>
    private static HashSet<string> Expected(List<Row> rows, string[] terms, out int viaAncestor)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var fullMask = (1 << terms.Length) - 1;
        viaAncestor = 0;

        foreach (var row in rows)
        {
            var nameMask = MaskOf(row.Name, terms);
            if (nameMask == 0)
                continue;

            var mask = nameMask;
            // Every segment above this row, plus "T:" -- the drive root is a segment the pass offers too.
            var segments = row.FullPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
                mask |= MaskOf(segments[i], terms);

            if (mask != fullMask)
                continue;

            expected.Add(row.FullPath);
            if (nameMask != fullMask)
                viaAncestor++;
        }
        return expected;
    }

    private static int MaskOf(string text, string[] terms)
    {
        var mask = 0;
        for (var i = 0; i < terms.Length; i++)
        {
            if (FuzzyMatcher.IsMatch(terms[i], text))
                mask |= 1 << i;
        }
        return mask;
    }

    private static HashSet<string> Search(LiveIndexFixture fixture, string query)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        // Far above anything these trees produce, so nothing is lost to the page limit and the
        // comparison is about what matches rather than about ranking.
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 5000, r => results.Add(r.Path), CancellationToken.None);
        return results;
    }

    // Trees deep enough that chains overlap and shallow enough to stay readable when one fails.
    private static List<Row> BuildTree(Random random)
    {
        var rows = new List<Row>();
        var folders = new List<(UInt128 Id, string Path)> { (1, "T:") };
        var nextId = (UInt128)2;

        var folderCount = random.Next(6, 16);
        for (var i = 0; i < folderCount; i++)
        {
            var (parentId, parentPath) = folders[random.Next(folders.Count)];
            var name = Words[random.Next(Words.Length)] + (random.Next(3) == 0 ? "" : "-" + random.Next(5));
            var path = parentPath + "\\" + name;
            rows.Add(new Row(nextId, parentId, name, true, path));
            folders.Add((nextId, path));
            nextId++;
        }

        var fileCount = random.Next(10, 40);
        for (var i = 0; i < fileCount; i++)
        {
            var (parentId, parentPath) = folders[random.Next(folders.Count)];
            var name = Words[random.Next(Words.Length)] + random.Next(20) + Extensions[random.Next(Extensions.Length)];
            rows.Add(new Row(nextId, parentId, name, false, parentPath + "\\" + name));
            nextId++;
        }

        return rows;
    }

    // Two and three terms: one is never routed here at all, and the mask is what the sharing is about,
    // so more than one term is the whole point.
    private static IEnumerable<string[]> QueriesFor(Random random)
    {
        for (var i = 0; i < 12; i++)
        {
            var count = random.Next(2, 4);
            var terms = new string[count];
            for (var t = 0; t < count; t++)
            {
                var word = Words[random.Next(Words.Length)];
                // Sometimes a prefix rather than the whole word, so a term can be satisfied by several
                // different segments at once.
                terms[t] = random.Next(2) == 0 ? word : word[..random.Next(2, word.Length + 1)];
            }
            if (terms.Distinct().Count() == terms.Length)
                yield return terms;
        }
    }
}
