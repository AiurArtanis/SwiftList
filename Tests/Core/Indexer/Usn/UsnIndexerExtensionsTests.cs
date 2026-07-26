using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Tests.IndexV2;

namespace SwiftList.Core.Tests.Indexer.Usn;

// ApplyFolderChange's own persist path (SaveDriveSnapshot -> LiveIndex.Compact) writes a real file under
// Logger.UserDataDir -- the same non-injectable-real-path hazard NetworkIndexerPublisherTests' own header
// comment calls out -- so, mirroring that suite, only the in-memory routing/bookkeeping (does the change
// land in the delta, does it flag or skip a persist) is covered here, not the actual disk write.
[TestClass]
public sealed class UsnIndexerExtensionsTests
{
    // Regression coverage for the local-drive counterpart of the network-drive rescan race: a
    // non-journaled drive's FolderDriveMonitor now stays alive for the whole rebuild (see
    // ApplyFolderChange's own comment on why), so a change landing mid-rebuild must be recorded as
    // missed instead of persisted against the doomed old LiveIndex -- ConsumeMissedFolderChangeDuringRebuild
    // is how the rebuild's own caller finds out it needs to queue a follow-up refresh.
    [TestMethod]
    public void ApplyFolderChange_DriveCurrentlyIndexing_AppliesChangeButFlagsItAsMissedInsteadOfPersisting()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        var (files, _) = fixture.Index.GetCounts();
        Assert.AreEqual(1, files);
        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    [TestMethod]
    public void ApplyFolderChange_DriveNotIndexing_DoesNotFlagAMissedChange()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });

        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    [TestMethod]
    public void ApplyFolderChange_UnknownDrive_DoesNotThrowOrFlagAMissedChange()
    {
        var indexer = new UsnIndexer();

        indexer.ApplyFolderChange("Z", WatcherChangeTypes.Changed, @"Z:\file.txt");

        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("Z"));
    }

    [TestMethod]
    public void ConsumeMissedFolderChangeDuringRebuild_NothingMissed_ReturnsFalse() =>
        Assert.IsFalse(new UsnIndexer().ConsumeMissedFolderChangeDuringRebuild("C"));

    [TestMethod]
    public void ConsumeMissedFolderChangeDuringRebuild_CalledTwice_OnlyTheFirstReturnsTrue()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        // Must not carry over to whatever the drive's next rebuild checks -- a stale true here would
        // queue a redundant follow-up refresh forever.
        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("swiftlist-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
