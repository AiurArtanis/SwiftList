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
        if (RemoveMatching(runtime, frn, parentFrn, name))
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

    /// <summary>FILE_DELETE / RENAME_OLD_NAME: remove the row for this exact link.</summary>
    public static void RemoveLink(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name)
    {
        RemoveMatching(runtime, frn, parentFrn, name);
        if (runtime.RowsForFrn(frn).Count == 0)
        {
            runtime.DeltaIdToIndex.Remove(frn);
            runtime.HardLinkDeltaRows.Remove(frn);
        }
    }

    private static bool RemoveMatching(RuntimeIndex runtime, UInt128 frn, UInt128 parentFrn, string name)
    {
        foreach (var row in runtime.RowsForFrn(frn))
        {
            if (Matches(runtime, row, parentFrn, name))
            {
                runtime.MarkRowDeleted(row);
                return true;
            }
        }
        return false;
    }

    private static bool Matches(RuntimeIndex runtime, int row, UInt128 parentFrn, string name)
    {
        var pi = runtime.ParentIndexes[row];
        var pf = pi >= 0 ? runtime.Ids[pi] : default;
        return pf == parentFrn && string.Equals(runtime.GetName(row), name, StringComparison.OrdinalIgnoreCase);
    }
}
