using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class IndexCacheManagerTests
{
    // Regression coverage: the root record used to default to LastWriteTimeUnixSeconds=0 and no Listed
    // flag, which meant TreeDiffBaseline.TryGetUnchangedChildren could never match it against a live stat
    // -- permanently forcing a full re-list of the root's own children on every resume. Both real USN/MFT
    // drives (via MftIndexScanner) and ReFsScanner share this same root-record construction.
    [TestMethod]
    public void CreateEmptyStore_RealDrive_StampsRootWithLiveMtimeAndListedFlag()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)![0].ToString();

        var store = IndexCacheManager.CreateEmptyStore(systemDrive, rootFrn: 1, nextUsn: 0, journalId: 0);

        var root = store.Records.Single();
        Assert.IsTrue(root.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.AreNotEqual(0u, root.LastWriteTimeUnixSeconds);
    }

    [TestMethod]
    public void CreateEmptyStore_UnresolvableDrive_StillSetsListedButLeavesMtimeZero()
    {
        var store = IndexCacheManager.CreateEmptyStore("~", rootFrn: 1, nextUsn: 0, journalId: 0);

        var root = store.Records.Single();
        Assert.IsTrue(root.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.AreEqual(0u, root.LastWriteTimeUnixSeconds);
    }
}
