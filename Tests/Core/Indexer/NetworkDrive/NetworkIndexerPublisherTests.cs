using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core.Tests.Indexer.NetworkDrive;

// Only exercises the pure dictionary/delegate bookkeeping paths. PublishIncrementalUpdate, and
// PublishCheckpoint's own non-alreadyComplete branch, both call IndexerHelper.Save, which ultimately
// writes a real snapshot file under Logger.UserDataDir (NetworkIndex.FromStore/SaveToCache) -- the same
// non-injectable-real-path hazard as UserSettings, so those are deliberately left untested here.
// PublishCheckpoint's alreadyComplete branch is the exception: it now returns before touching disk at
// all (see PublishCheckpoint's own comment on why that check moved earlier), so it's covered below.
// `new NetworkIndex(drive)` (no _live set) is safe to use as a stand-in index: Count is 0, ToStore()/
// Dispose() both short-circuit on _live == null without touching disk.
[TestClass]
public sealed class NetworkIndexerPublisherTests
{
    private sealed class Fixture
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, NetworkIndexStatus> Statuses = new();
        public readonly Dictionary<string, NetworkIndex> Indexes = new();
        public readonly List<string> WatchersEnsured = new();
        public int StatusesChangedCount;

        public NetworkIndexerPublisher CreatePublisher() => new(
            Gate, Statuses, Indexes,
            drive => WatchersEnsured.Add(drive),
            () => Statuses.Values.ToList(),
            _ => StatusesChangedCount++);
    }

    [TestMethod]
    public void SetStatus_UnknownDrive_DoesNothingAndDoesNotPublish()
    {
        var fixture = new Fixture();
        var publisher = fixture.CreatePublisher();

        publisher.SetStatus("Z", "indexing", 5, null);

        Assert.IsFalse(fixture.Statuses.ContainsKey("Z"));
        Assert.AreEqual(0, fixture.StatusesChangedCount);
    }

    [TestMethod]
    public void SetStatus_KnownDrive_UpdatesStatusAndPublishes()
    {
        var fixture = new Fixture();
        fixture.Statuses["Z"] = new NetworkIndexStatus { Drive = "Z", State = "pending", Items = 0 };
        var publisher = fixture.CreatePublisher();

        publisher.SetStatus("Z", "indexing", 7, "oops");

        Assert.AreEqual("indexing", fixture.Statuses["Z"].State);
        Assert.AreEqual(7, fixture.Statuses["Z"].Items);
        Assert.AreEqual("oops", fixture.Statuses["Z"].Error);
        Assert.AreEqual(1, fixture.StatusesChangedCount);
    }

    [TestMethod]
    public void SetStatus_NullItems_KeepsPreviousItemCount()
    {
        var fixture = new Fixture();
        fixture.Statuses["Z"] = new NetworkIndexStatus { Drive = "Z", Items = 42 };
        var publisher = fixture.CreatePublisher();

        publisher.SetStatus("Z", "indexing", items: null, error: null);

        Assert.AreEqual(42, fixture.Statuses["Z"].Items);
    }

    [TestMethod]
    public void GetPreviousStore_UnknownDrive_ReturnsNull()
    {
        var fixture = new Fixture();
        var publisher = fixture.CreatePublisher();

        Assert.IsNull(publisher.GetPreviousStore("Z"));
    }

    [TestMethod]
    public void GetPreviousStore_KnownDrive_ReturnsStoreFromIndex()
    {
        var fixture = new Fixture();
        fixture.Indexes["Z"] = new NetworkIndex("Z");
        var publisher = fixture.CreatePublisher();

        var store = publisher.GetPreviousStore("Z");

        Assert.IsNotNull(store);
        Assert.AreEqual("Z", store.SourceKey);
    }

    [TestMethod]
    public void OnRefreshFinished_DriveNotTracked_DoesNotStoreOrPublish()
    {
        var fixture = new Fixture();
        var publisher = fixture.CreatePublisher();

        publisher.OnRefreshFinished("Z", new NetworkIndex("Z"));

        Assert.IsFalse(fixture.Indexes.ContainsKey("Z"));
        Assert.AreEqual(0, fixture.StatusesChangedCount);
        Assert.IsEmpty(fixture.WatchersEnsured);
    }

    [TestMethod]
    public void OnRefreshFinished_DriveTracked_StoresNewIndexAndEnsuresWatcher()
    {
        var fixture = new Fixture();
        fixture.Statuses["Z"] = new NetworkIndexStatus { Drive = "Z" };
        var oldIndex = new NetworkIndex("Z");
        fixture.Indexes["Z"] = oldIndex;
        var publisher = fixture.CreatePublisher();
        var newIndex = new NetworkIndex("Z");

        publisher.OnRefreshFinished("Z", newIndex);

        Assert.AreSame(newIndex, fixture.Indexes["Z"]);
        Assert.AreEqual("ready", fixture.Statuses["Z"].State);
        CollectionAssert.Contains(fixture.WatchersEnsured, "Z");
        Assert.AreEqual(1, fixture.StatusesChangedCount);
    }

    [TestMethod]
    public void PublishCheckpoint_CachedIndexAlreadyComplete_SkipsPersistingAndOnlyUpdatesStatus()
    {
        var fixture = new Fixture();
        var completeIndex = new NetworkIndex("Z") { IsComplete = true };
        fixture.Indexes["Z"] = completeIndex;
        fixture.Statuses["Z"] = new NetworkIndexStatus { Drive = "Z", State = "ready" };
        var publisher = fixture.CreatePublisher();

        var store = new FileRecordStore { SourceKey = "Z" };
        store.Records.Add(new FileRecord(1, 1, "Z:\\", FileRecordFlags.Directory)); // root -- excluded from the count
        store.Records.Add(new FileRecord(2, 1, "file.txt", FileRecordFlags.None));
        store.Records.Add(new FileRecord(3, 1, "deleted.txt", FileRecordFlags.Deleted)); // excluded, soft-deleted

        publisher.PublishCheckpoint("Z", store, default, CancellationToken.None);

        // Never regressed back to the smaller in-progress view, and never touched (let alone disposed)
        // the cached complete index -- see PublishCheckpoint's own comment on why this check now runs
        // before NetworkIndex.FromStore would otherwise write straight over its still-mapped cache file.
        Assert.AreSame(completeIndex, fixture.Indexes["Z"]);
        Assert.AreEqual("indexing", fixture.Statuses["Z"].State);
        Assert.AreEqual(1, fixture.Statuses["Z"].Items);
        Assert.AreEqual(1, fixture.StatusesChangedCount);
    }

    [TestMethod]
    public void PublishCheckpoint_DriveRemovedFromConfigWhileCachedIndexComplete_DoesNotResurrectStatus()
    {
        var fixture = new Fixture();
        fixture.Indexes["Z"] = new NetworkIndex("Z") { IsComplete = true };
        // No Statuses["Z"] entry -- mirrors Configure() having already deleted it.
        var publisher = fixture.CreatePublisher();

        publisher.PublishCheckpoint("Z", new FileRecordStore { SourceKey = "Z" }, default, CancellationToken.None);

        Assert.IsFalse(fixture.Statuses.ContainsKey("Z"));
        Assert.AreEqual(0, fixture.StatusesChangedCount);
    }

    // Regression coverage for the checkpoint/final-write "file to be replaced is in use" failure: the
    // currently-cached index must be gone from _indexes (not just disposed in place) the moment this
    // returns, since NetworkIndex.Build's beforeFinalWrite callback (wired to this method) relies on it
    // to free that drive's cache path before writing a fresh file over it.
    [TestMethod]
    public void ReleaseCachedIndex_DriveHasACachedIndex_RemovesAndReturnsIt()
    {
        var fixture = new Fixture();
        var cached = new NetworkIndex("Z");
        fixture.Indexes["Z"] = cached;
        var publisher = fixture.CreatePublisher();

        var released = publisher.ReleaseCachedIndex("Z");

        Assert.AreSame(cached, released);
        Assert.IsFalse(fixture.Indexes.ContainsKey("Z"));
    }

    [TestMethod]
    public void ReleaseCachedIndex_DriveHasNoCachedIndex_ReturnsNull()
    {
        var fixture = new Fixture();
        var publisher = fixture.CreatePublisher();

        Assert.IsNull(publisher.ReleaseCachedIndex("Z"));
    }
}
