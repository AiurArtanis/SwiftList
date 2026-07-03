using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

/// <summary>
/// Incremental one-to-many hard-link maintenance. On a USN_REASON_HARD_LINK_CHANGE the reason alone
/// can't tell whether a link was added or removed, so we re-enumerate the file's current links
/// (FindFirstFileNameW) and diff them against the rows the index holds for that FRN: links present
/// on disk but missing get appended, rows whose link is gone get marked deleted.
/// </summary>
internal static class HardLinkDelta
{
    public static void ApplyDiff(RuntimeIndex runtime, string drive, UInt128 frn)
    {
        var rows = runtime.RowsForFrn(frn);
        var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            indexByPath[runtime.GetFullPath(row)] = row;

        // FindFirstFileNameW needs a path to seed on; use any indexed link that still exists on disk.
        string? seed = null;
        foreach (var p in indexByPath.Keys)
        {
            if (File.Exists(p) || Directory.Exists(p))
            {
                seed = p;
                break;
            }
        }

        var current = seed != null ? EnumerateCurrentLinks(seed, drive) : null;
        var currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (current != null)
            foreach (var c in current)
                currentSet.Add(c);

        // Remove rows whose link no longer exists on disk.
        foreach (var kv in indexByPath)
        {
            if (!currentSet.Contains(kv.Key))
                runtime.MarkRowDeleted(kv.Value);
        }

        if (currentSet.Count == 0)
            return;

        var flags = rows.Count > 0 ? runtime.GetFlags(rows[0]) : FlagsFromDisk(seed);
        foreach (var path in currentSet)
        {
            if (indexByPath.ContainsKey(path))
                continue;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
                continue;
            if (!runtime.TryResolvePath(dir.ToLowerInvariant(), out var parentId, out var childPrefix) || childPrefix.Length != 0)
                continue; // parent directory not (fully) in the index
            runtime.AppendHardLink(new FileRecord(frn, parentId, name, flags));
        }
    }

    /// <summary>Full paths of every hard link of the file at <paramref name="seedPath"/>, or null if it can't be opened.</summary>
    private static List<string>? EnumerateCurrentLinks(string seedPath, string drive)
    {
        uint len = 32768;
        var buf = new char[len];
        var h = Win32Api.FindFirstFileNameW(seedPath, 0, ref len, buf);
        if (h == new IntPtr(-1))
            return null;

        var res = new List<string>();
        try
        {
            do
            {
                var nul = Array.IndexOf(buf, '\0');
                var volRel = new string(buf, 0, nul < 0 ? buf.Length : nul); // e.g. "\Windows\explorer.exe"
                res.Add(drive + ":" + volRel);
                len = 32768;
            } while (Win32Api.FindNextFileNameW(h, ref len, buf));
        }
        finally
        {
            Win32Api.FindClose(h);
        }
        return res;
    }

    private static FileRecordFlags FlagsFromDisk(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return FileRecordFlags.None;
        try
        {
            return FileRecordFlagsHelper.FromAttributes(File.GetAttributes(path));
        }
        catch
        {
            return FileRecordFlags.None;
        }
    }
}
