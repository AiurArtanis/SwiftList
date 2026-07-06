using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

/// <summary>
/// Precise one-to-many incremental maintenance, keyed by the (FRN, parent, name) a USN record
/// already carries — no FindFirstFileNameW / disk probing needed. Each hard link is its own row, so
/// a rename/delete/create of one link only touches that link's row and leaves the file's other
/// links alone.
/// </summary>
internal static class HardLinkDelta
{
    /// <summary>HARD_LINK_CHANGE: the reason can't say add vs remove, so toggle — if this exact link
    /// is already indexed it was removed; otherwise it was added.</summary>
    public static void ToggleLink(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name, FileRecordFlags flags)
    {
        if (RemoveMatching(runtime, frn, parentFrn, name, isRename: false))
            return;
        runtime.AppendHardLink(new FileRecord(frn, parentFrn, name, flags));
    }

    /// <summary>FILE_CREATE / RENAME_NEW_NAME: ensure a row for this exact link exists.</summary>
    public static void AddLink(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name, FileRecordFlags flags)
    {
        foreach (var row in runtime.RowsForFrn(frn))
            if (Matches(runtime, row, parentFrn, name))
                return; // already present
        runtime.AppendHardLink(new FileRecord(frn, parentFrn, name, flags));
    }

    /// <summary>FILE_DELETE: remove the row for this exact link, cascading to children if it's a directory.</summary>
    public static void RemoveLink(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name)
    {
        RemoveMatching(runtime, frn, parentFrn, name, isRename: false);
        PruneIfUnused(runtime, frn);
    }

    /// <summary>RENAME_OLD_NAME: unlike a real delete, the FRN survives under a new name (a matching
    /// RENAME_NEW_NAME follows), so a directory's children must NOT be cascade-removed. Re-orphan them
    /// instead -- ReparentWaitingOrphans re-links them the moment AddLink adds the renamed row for the
    /// same FRN.</summary>
    public static void RemoveLinkForRename(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name)
    {
        RemoveMatching(runtime, frn, parentFrn, name, isRename: true);
        PruneIfUnused(runtime, frn);
    }

    private static void PruneIfUnused(RuntimeIndex runtime, UInt128 frn)
    {
        if (runtime.RowsForFrn(frn).Count == 0)
        {
            runtime.DeltaIdToIndex.Remove(frn);
            runtime.HardLinkDeltaRows.Remove(frn);
        }
    }

    private static bool RemoveMatching(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name, bool isRename)
    {
        foreach (var row in runtime.RowsForFrn(frn))
        {
            if (Matches(runtime, row, parentFrn, name))
            {
                var wasDirectory = runtime.IsDirectory(row);
                runtime.MarkRowDeleted(row);
                if (wasDirectory)
                {
                    if (isRename)
                        ReorphanChildren(runtime, row, frn);
                    else
                        CascadeDeleteChildren(runtime, row);
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>A directory's own USN delete record says nothing about its children, so without this
    /// they'd stay indexed as live rows forever (until a full rebuild) -- still searchable, and immune
    /// to further updates since nothing else ever revisits them.</summary>
    private static void CascadeDeleteChildren(RuntimeIndex runtime, int parentIdx)
    {
        if (!runtime.ParentToChildren.TryGetValue(parentIdx, out var children) || children.Count == 0)
            return;

        // MarkRowDeleted removes the child from this very list, so iterate a snapshot.
        foreach (var childIdx in children.ToArray())
        {
            if (runtime.IsDeleted(childIdx))
                continue;
            var childIsDirectory = runtime.IsDirectory(childIdx);
            runtime.MarkRowDeleted(childIdx);
            if (childIsDirectory)
                CascadeDeleteChildren(runtime, childIdx);
        }
    }

    /// <summary>Only direct children need re-orphaning: grandchildren are still correctly linked to
    /// their own (untouched) immediate parent row regardless of what the top directory is renamed to.</summary>
    private static void ReorphanChildren(RuntimeIndex runtime, int parentIdx, UInt128 frn)
    {
        if (!runtime.ParentToChildren.TryGetValue(parentIdx, out var children) || children.Count == 0)
            return;

        foreach (var childIdx in children.ToArray())
        {
            if (runtime.IsDeleted(childIdx))
                continue;
            runtime.ParentIndexes[childIdx] = -1;
            runtime.TrackOrphanParent(childIdx, -1, frn);
        }
        runtime.ParentToChildren.Remove(parentIdx);
        runtime.PathMemo.Clear();
    }

    private static bool Matches(RuntimeIndex runtime, int row, UInt128 parentFrn, string name)
    {
        var pi = runtime.ParentIndexes[row];
        var pf = pi >= 0 ? runtime.Ids[pi] : default;
        return pf == parentFrn && string.Equals(runtime.GetName(row), name, StringComparison.OrdinalIgnoreCase);
    }
}
