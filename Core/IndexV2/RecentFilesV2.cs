using SwiftList.PluginSdk.Abstractions;

using SwiftList.Core.IndexV2.Delta;

using SwiftList.Core.IndexV2.Search;

using SwiftList.Core.IndexV2.Persistence;
namespace SwiftList.Core.IndexV2;

// Mirrors RecentFilesWalker.CollectFromDirectory over Snapshot+DeltaOverlay: an in-memory subtree DFS
// via the CSR children column, base rows plus delta rows relocated into the walked subtree, capped at
// the same per-directory scan limit so a target accidentally set to a whole drive can't runaway.
public static class RecentFilesV2
{
    public const int MaxScannedPerDirectory = 200_000;

    public static void CollectFromDirectory(Snapshot snapshot, DeltaOverlay delta, string dirLower, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (!DirectoryFilterResolver.TryResolve(snapshot, delta, dirLower, forceLastSegmentAsQuery: false, out var rootRow, out var remainder)
            || remainder.Length > 0 || !snapshot.IsDirectory(rootRow) || delta.IsSuperseded(rootRow))
            return;

        var stack = new Stack<int>();
        stack.Push(rootRow);
        var scanned = 0;
        while (stack.Count > 0 && scanned < MaxScannedPerDirectory)
        {
            var current = stack.Pop();
            foreach (var child in snapshot.ChildrenOf(current))
            {
                if (snapshot.IsDeleted(child) || delta.IsSuperseded(child))
                    continue;
                scanned++;
                if (snapshot.IsDirectory(child))
                    stack.Push(child);
                Emit(snapshot, delta, child, drive, cutoffUtc, candidates);
            }
            foreach (var (row, record) in delta.BaseOverrides)
            {
                if (record.ParentBaseRow != current)
                    continue;
                scanned++;
                if ((record.Flags & (ushort)FileRecordFlags.Directory) != 0)
                    stack.Push(row);
                EmitOverride(delta, row, record, drive, cutoffUtc, candidates);
            }
            var currentFrn = snapshot.Ids[current];
            foreach (var record in delta.Added)
            {
                if (record.Removed || record.ParentFrn != currentFrn)
                    continue;
                scanned++;
                if (record.LastWrite >= cutoffUtc)
                    candidates.Add(ToResult(record.Name, delta.GetFullPath(record), (record.Flags & (ushort)FileRecordFlags.Directory) != 0, drive, record.LastWrite));
            }
        }
    }

    private static void Emit(Snapshot snapshot, DeltaOverlay delta, int row, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        var (_, _, lastWrite, _) = delta.MetadataOf(row);
        if (lastWrite < cutoffUtc)
            return;
        candidates.Add(ToResult(delta.NameOf(row), delta.GetFullPath(row), snapshot.IsDirectory(row), drive, lastWrite));
    }

    private static void EmitOverride(DeltaOverlay delta, int row, DeltaOverlay.DeltaRecord record, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (record.LastWrite < cutoffUtc)
            return;
        candidates.Add(ToResult(record.Name, delta.GetFullPath(row), (record.Flags & (ushort)FileRecordFlags.Directory) != 0, drive, record.LastWrite));
    }

    // Size/Created/Accessed aren't tracked here -- Recent Files only ever needs Modified, for the
    // recency merge/sort (see SearchEngineRecentFilesExtensions/SearchService.GetRecentFilesAsync).
    private static SearchResult ToResult(string name, string path, bool isDir, string drive, uint modifiedUtc) => new()
    {
        Name = name,
        Path = path,
        IsDir = isDir,
        Drive = drive,
        Metadata = new FileMetadata(0, DateTime.MinValue, FileTimeHelper.FromUnixSeconds(modifiedUtc).ToLocalTime(), DateTime.MinValue),
    };
}
