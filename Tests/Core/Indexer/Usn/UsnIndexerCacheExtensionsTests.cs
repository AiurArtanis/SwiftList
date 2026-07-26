using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.Tests.Indexer.Usn;

// TryOpenV2/LoadDrivesFromCache/TryLoadDriveFromCache/DropDriveFromRuntime all need a real V2 cache file on
// disk (SnapshotWriter.Write + Snapshot.Open), so they aren't covered here -- see LiveIndexFixture-based
// tests elsewhere for that. IsDriveIndexComplete is pure dictionary bookkeeping and gets direct coverage.
[TestClass]
public sealed class UsnIndexerCacheExtensionsTests
{
    [TestMethod]
    public void IsDriveIndexComplete_NoMetadataLoadedForDrive_ReturnsFalse() =>
        Assert.IsFalse(new UsnIndexer().IsDriveIndexComplete("C"));

    [TestMethod]
    public void IsDriveIndexComplete_MetadataMarkedComplete_ReturnsTrue()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = true };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
    }

    [TestMethod]
    public void IsDriveIndexComplete_MetadataMarkedIncomplete_ReturnsFalse()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false };

        Assert.IsFalse(indexer.IsDriveIndexComplete("C"));
    }

    [TestMethod]
    public void IsDriveIndexComplete_OnlyChecksTheNamedDrive()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = true };
        indexer._driveMetadata["D"] = new UsnIndexer.DriveRuntimeMetadata { IsComplete = false };

        Assert.IsTrue(indexer.IsDriveIndexComplete("C"));
        Assert.IsFalse(indexer.IsDriveIndexComplete("D"));
    }
}
