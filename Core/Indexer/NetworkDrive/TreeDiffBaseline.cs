namespace SwiftList.Core.Indexer.NetworkDrive;

// Wraps a previously-saved FileRecordStore (a completed index, or an interrupted checkpoint) as a lookup
// baseline for TreeBuilder's diff-aware walk. A directory's cached children are only trusted when it was
// fully enumerated last time (FileRecordFlags.Listed) AND its own LastWriteTimeUnixSeconds still matches
// live -- meaning nothing was added, removed, or renamed directly under it since. This does NOT guarantee
// deeper descendants are unchanged (a directory's own mtime never reflects grandchild changes), so the
// caller still recurses into and individually checks every cached child directory.
//
// Indexes by position into the caller's own previousStore.Records rather than copying FileRecord values
// into these dictionaries -- for a multi-million-record NAS this is the difference between a few bytes of
// overhead per record (an int) and duplicating every record's full ~64-byte payload twice over (once in
// _indexById, once again spread across _childIndicesByParent's lists), on top of memory this diff pass
// didn't otherwise need to hold.
internal sealed class TreeDiffBaseline
{
    private readonly IReadOnlyList<FileRecord> _records;
    private readonly Dictionary<UInt128, int> _indexById = new();
    private readonly Dictionary<UInt128, List<int>> _childIndicesByParent = new();

    private TreeDiffBaseline(IReadOnlyList<FileRecord> records) => _records = records;

    public static TreeDiffBaseline? From(FileRecordStore? previousStore)
    {
        if (previousStore == null || previousStore.Records.Count == 0)
            return null;

        var baseline = new TreeDiffBaseline(previousStore.Records);
        for (var i = 0; i < previousStore.Records.Count; i++)
            baseline._indexById[previousStore.Records[i].Id] = i;

        // Second pass, gated on _indexById: a store that (through some earlier bug) ended up with more than
        // one row for the same id must only ever contribute ONE entry to its parent's child list here --
        // otherwise the caller enqueues that same directory id more than once, and each duplicate re-walks
        // (or re-copies) its entire subtree again, compounding into unbounded, runaway growth on every
        // subsequent resume instead of just carrying the original duplication forward unchanged.
        for (var i = 0; i < previousStore.Records.Count; i++)
        {
            var record = previousStore.Records[i];
            if (baseline._indexById[record.Id] != i)
                continue;

            if (!baseline._childIndicesByParent.TryGetValue(record.ParentId, out var siblings))
                baseline._childIndicesByParent[record.ParentId] = siblings = new List<int>();
            siblings.Add(i);
        }
        return baseline;
    }

    public bool TryGetUnchangedChildren(string physicalPath, UInt128 directoryId, out IEnumerable<FileRecord> children)
    {
        children = Enumerable.Empty<FileRecord>();

        if (!_indexById.TryGetValue(directoryId, out var recordIndex))
            return false;

        var record = _records[recordIndex];
        if (!record.IsDirectory || (record.Flags & FileRecordFlags.Listed) == 0)
            return false;

        uint liveMtime;
        try
        {
            liveMtime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(physicalPath));
        }
        catch
        {
            return false;
        }

        if (liveMtime != record.LastWriteTimeUnixSeconds)
            return false;

        children = EnumerateChildren(directoryId);
        return true;
    }

    private IEnumerable<FileRecord> EnumerateChildren(UInt128 directoryId)
    {
        if (!_childIndicesByParent.TryGetValue(directoryId, out var indices))
            yield break;

        foreach (var index in indices)
            yield return _records[index];
    }
}
