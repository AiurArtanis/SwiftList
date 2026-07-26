using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.IndexV2.Persistence;

namespace SwiftList.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class NetworkIndexTests
{
    // Regression coverage: Dispose() used to only call _live?.Dispose() without clearing the field, so
    // every _live-touching method's own "if (_live == null) return;" guard (Count, SaveToCache,
    // SearchStreaming, GetRecentFiles, ApplyCreatedOrChanged/Deleted/Renamed) silently assumed a disposed
    // instance would look null and no-op -- instead it stayed non-null and pointed at an already-disposed
    // LiveIndex, so calling any of them again threw ObjectDisposedException instead of no-op'ing. This
    // became reachable in practice once WatcherManager started debouncing its publish call: a watcher-
    // detected change can now be scheduled up to a second before it's actually persisted, widening the
    // window for PublishCheckpoint's ReleaseCachedIndex to dispose this same instance first.
    [TestMethod]
    public void Dispose_ThenSaveToCacheAgain_DoesNotThrow()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();

        index.SaveToCache(path);
    }

    [TestMethod]
    public void Dispose_ThenReadCount_ReturnsZeroInsteadOfThrowing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();

        Assert.AreEqual(0, index.Count);
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "test.idx");
        SnapshotWriter.Write(BuildStore("Z"), path);
        var index = NetworkIndex.FromSnapshotFile("Z", path);

        index.Dispose();
        index.Dispose();
    }

    private static FileRecordStore BuildStore(string drive)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        return store;
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
