using SwiftList.Core.Services.Search;

namespace SwiftList.Core.Tests.Services.Search;

[TestClass]
public sealed class LiveDirectorySearcherTests
{
    [TestMethod]
    public void ScanDirectory_EmptyPath_ReturnsEmpty()
    {
        var results = LiveDirectorySearcher.ScanDirectory("", 100, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void ScanDirectory_NonExistentPath_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "swiftlist-tests-nonexistent-dir-marker");

        var results = LiveDirectorySearcher.ScanDirectory(path, 100, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void ScanDirectory_FileAndSubdirectory_ReturnsBothWithCorrectMetadata()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None);

        Assert.HasCount(2, results);
        var file = results.Single(r => r.Name == "a.txt");
        Assert.IsFalse(file.IsDir);
        var subdir = results.Single(r => r.Name == "sub");
        Assert.IsTrue(subdir.IsDir);
    }

    [TestMethod]
    public void ScanDirectory_RecursesIntoSubdirectories()
    {
        using var dir = new TempDirectory();
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "x");

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 100, CancellationToken.None);

        Assert.IsTrue(results.Any(r => r.Name == "nested.txt"));
    }

    [TestMethod]
    public void ScanDirectory_MaxProcessedLimitsResultCount()
    {
        using var dir = new TempDirectory();
        for (var i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(dir.Path, $"file{i}.txt"), "x");

        var results = LiveDirectorySearcher.ScanDirectory(dir.Path, 3, CancellationToken.None);

        Assert.IsLessThanOrEqualTo(3, results.Count);
    }

    [TestMethod]
    public void MatchAndStream_EmptyEntries_ReturnsFalse()
    {
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(new List<SearchResult>(), "query", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_NoQuery_StreamsEveryEntry()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "alpha.txt", Path = @"C:\alpha.txt" },
            new() { Name = "beta.txt", Path = @"C:\beta.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(2, streamed);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesSubsequence_StreamsOnlyMatchingEntries()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "readme.txt", Path = @"C:\readme.txt" },
            new() { Name = "other.log", Path = @"C:\other.log" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "read", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.txt", streamed[0].Name);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesNothing_ReturnsFalse()
    {
        var entries = new List<SearchResult> { new() { Name = "readme.txt", Path = @"C:\readme.txt" } };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "zzz", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_OnlyDirectChildren_FiltersOutGrandchildren()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "child.txt", Path = @"C:\root\child.txt" },
            new() { Name = "grandchild.txt", Path = @"C:\root\sub\grandchild.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None,
            onlyDirectChildren: true, parentPath: @"C:\root");

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("child.txt", streamed[0].Name);
    }

    [TestMethod]
    public void ResolvePathModeSearch_EmptyInput_ReturnsEmptyTuple()
    {
        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch("");

        Assert.AreEqual(string.Empty, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_ExistingDirectory_ReturnsItselfWithNoFilter()
    {
        using var tempDir = new TempDirectory();

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch(tempDir.Path);

        Assert.AreEqual(tempDir.Path, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_NonExistentSubPath_ReturnsNearestExistingAncestorAndFilter()
    {
        using var tempDir = new TempDirectory();
        var target = Path.Combine(tempDir.Path, "missing-sub", "file.txt");

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch(target);

        Assert.AreEqual(tempDir.Path, dir);
        Assert.AreEqual(Path.Combine("missing-sub", "file.txt"), filter);
    }

    [TestMethod]
    public void ResolvePathModeSearch_NoAncestorExists_ReturnsEmptyTuple()
    {
        var usedLetters = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var freeLetter = Enumerable.Range('A', 26).Select(c => (char)c).First(c => !usedLetters.Contains(c));

        var (dir, filter) = LiveDirectorySearcher.ResolvePathModeSearch($@"{freeLetter}:\definitely-not-real\deeper\file.txt");

        Assert.AreEqual(string.Empty, dir);
        Assert.AreEqual(string.Empty, filter);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("swiftlist-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
