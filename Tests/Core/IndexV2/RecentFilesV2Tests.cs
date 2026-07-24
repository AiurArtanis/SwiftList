using SwiftList.Core.IndexV2;

namespace SwiftList.Core.Tests.IndexV2;

[TestClass]
public sealed class RecentFilesV2Tests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "old.txt", FileRecordFlags.None, lastWriteTimeUnixSeconds: 100),
        new FileRecord(4, 2, "new.txt", FileRecordFlags.None, lastWriteTimeUnixSeconds: 1000),
        new FileRecord(5, 2, "sub", FileRecordFlags.Directory),
        new FileRecord(6, 5, "nested.txt", FileRecordFlags.None, lastWriteTimeUnixSeconds: 2000),
    });

    [TestMethod]
    public void CollectFromDirectory_OnlyIncludesEntriesAtOrAfterCutoff()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\", "C", cutoffUtc: 500, candidates);

            var names = candidates.Select(c => c.Name).ToList();
            CollectionAssert.Contains(names, "new.txt");
            CollectionAssert.Contains(names, "nested.txt");
            CollectionAssert.DoesNotContain(names, "old.txt");
            return 0;
        });
    }

    [TestMethod]
    public void CollectFromDirectory_ScopedToSubdirectory_ExcludesSiblingFiles()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\sub\", "C", cutoffUtc: 0, candidates);

            var names = candidates.Select(c => c.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "nested.txt" }, names);
            return 0;
        });
    }

    [TestMethod]
    public void CollectFromDirectory_IncludesDeltaAddedFileInScope()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Upsert(200, 2, "fresh.txt", FileRecordFlags.None, 1, 0, 3000, 0);

            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\", "C", cutoffUtc: 500, candidates);

            CollectionAssert.Contains(candidates.Select(c => c.Name).ToList(), "fresh.txt");
        });
    }

    [TestMethod]
    public void CollectFromDirectory_ExcludesDeletedFileEvenWithinCutoff()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Remove(4); // "new.txt", lastWrite=1000

            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\", "C", cutoffUtc: 500, candidates);

            CollectionAssert.DoesNotContain(candidates.Select(c => c.Name).ToList(), "new.txt");
        });
    }

    [TestMethod]
    public void CollectFromDirectory_UnresolvableDirectory_ReturnsEmpty()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\does-not-exist\", "C", cutoffUtc: 0, candidates);

            Assert.IsEmpty(candidates);
            return 0;
        });
    }

    [TestMethod]
    public void CollectFromDirectory_ZeroCutoff_IncludesEverythingInScope()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Read((snapshot, delta) =>
        {
            var candidates = new List<SearchResult>();
            RecentFilesV2.CollectFromDirectory(snapshot, delta, @"c:\projects\", "C", cutoffUtc: 0, candidates);

            var names = candidates.Select(c => c.Name).ToList();
            CollectionAssert.Contains(names, "old.txt");
            CollectionAssert.Contains(names, "new.txt");
            CollectionAssert.Contains(names, "nested.txt");
            return 0;
        });
    }
}
