namespace SwiftList.Core.SearchIndex.RecordIndex;

/// <summary>
/// One-to-many incremental primitives for hard links. Unlike <see cref="UpdateExtensions.Upsert"/>
/// (which assumes one row per FRN and replaces in place), these let a single FRN own several rows —
/// one per hard link. They deliberately do NOT touch DeltaIdToIndex, so the existing one-to-one
/// TryGetIndexById / parent-resolution paths are unchanged; search finds appended rows by scanning.
/// </summary>
internal static class HardLinkUpdateExtensions
{
    /// <summary>All non-deleted rows that belong to <paramref name="frn"/>: the sorted loaded region
    /// (binary-searched range) plus any delta rows appended for this FRN.</summary>
    internal static List<int> RowsForFrn(this RuntimeIndex index, UInt128 frn)
    {
        var result = new List<int>();
        var ids = index.Ids;
        for (var i = LowerBound(ids, frn, index.LoadedCount); i < index.LoadedCount && ids[i] == frn; i++)
            if (!index.IsDeleted(i))
                result.Add(i);

        // A single delta row created by the regular one-to-one Upsert (e.g. a freshly created file
        // or a rename target). Without this the diff can't see links the index already holds.
        if (index.DeltaIdToIndex.TryGetValue(frn, out var single) && !index.IsDeleted(single))
            result.Add(single);

        if (index.HardLinkDeltaRows.TryGetValue(frn, out var delta))
            foreach (var i in delta)
                if (!index.IsDeleted(i))
                    result.Add(i);

        return result;
    }

    /// <summary>Appends a new row for an existing (or new) hard-linked FRN — one row per link.</summary>
    internal static int AppendHardLink(this RuntimeIndex index, FileRecord record)
    {
        var idx = index.Count;
        index.AddColumns(record.Id, record.Name, record.Flags, record.Size, record.CreationTimeUnixSeconds, record.LastWriteTimeUnixSeconds, record.LastAccessTimeUnixSeconds);

        var parentIndex = index.ResolveParentIndex(record.Id, record.ParentId);
        index.ParentIndexes.Add(parentIndex);
        index.TrackOrphanParent(idx, parentIndex, record.ParentId);
        if (parentIndex >= 0)
        {
            if (!index.ParentToChildren.TryGetValue(parentIndex, out var list))
            {
                list = new List<int>();
                index.ParentToChildren[parentIndex] = list;
            }
            list.Add(idx);
        }

        // This new row may be the parent earlier out-of-order rows were orphaned waiting for.
        index.ReparentWaitingOrphans(idx, record.Id);

        if (record.IsDirectory)
            index.TotalDirs++;
        else
            index.TotalFiles++;

        var aliases = index.GenerateAliases(record.Name, out var providerIds);
        if (aliases != null && aliases.Length > 0)
        {
            index.DeltaNameAliases[idx] = aliases;
            index.DeltaAliasProviderIds[idx] = providerIds;
            index.CharMasks[idx] = ulong.MaxValue;
        }
        index.AddNameCharDelta(record.Name, idx);

        if (!index.HardLinkDeltaRows.TryGetValue(record.Id, out var rows))
        {
            rows = new List<int>();
            index.HardLinkDeltaRows[record.Id] = rows;
        }
        rows.Add(idx);

        index.PathMemo.Clear();
        return idx;
    }

    /// <summary>Refreshes a row's Size/timestamp columns in place (e.g. after a USN content-only change)
    /// without touching its name, parent, or flags.</summary>
    internal static void UpdateMetadata(this RuntimeIndex index, int idx, long size, uint creationTimeUtc, uint lastWriteTimeUtc, uint lastAccessTimeUtc)
    {
        index.Sizes[idx] = size;
        index.CreationTimes[idx] = creationTimeUtc;
        index.LastWriteTimes[idx] = lastWriteTimeUtc;
        index.LastAccessTimes[idx] = lastAccessTimeUtc;
    }

    /// <summary>Marks a single row deleted (one hard link removed) without touching the FRN's other rows.</summary>
    internal static void MarkRowDeleted(this RuntimeIndex index, int idx)
    {
        if (index.IsDeleted(idx))
            return;

        var wasDirectory = index.IsDirectory(idx);
        index.Flags[idx] = (byte)(((FileRecordFlags)index.Flags[idx]) | FileRecordFlags.Deleted);
        index.CharMasks[idx] = 0;

        var parentIndex = index.ParentIndexes[idx];
        if (parentIndex >= 0 && index.ParentToChildren.TryGetValue(parentIndex, out var list))
            list.Remove(idx);

        if (wasDirectory)
            index.TotalDirs = Math.Max(0, index.TotalDirs - 1);
        else
            index.TotalFiles = Math.Max(0, index.TotalFiles - 1);

        index.PathMemo.Clear();
    }

    private static int LowerBound(List<UInt128> ids, UInt128 frn, int count)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (ids[mid] < frn)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
