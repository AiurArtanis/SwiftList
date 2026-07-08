namespace SwiftList.Core.SearchIndex.RecordIndex;

// Shared subtree walk behind GetRecentFiles, used by both the local NTFS/ReFS path (UsnIndexer, one
// RuntimeIndex per drive letter) and the network/WSL path (NetworkIndex, one RuntimeIndex per configured
// share) -- the walk itself doesn't care which kind of RuntimeIndex it's given.
public static class RecentFilesWalker
{
    // Safety cap per directory so a target that's accidentally set to something huge (a whole drive)
    // can't turn a "show me recent files" query into an unbounded full-volume walk.
    public const int MaxScannedPerDirectory = 200_000;

    public static void CollectFromDirectory(RuntimeIndex index, string dirLower, string drive, uint cutoffUtc, List<SearchResult> candidates)
    {
        if (!index.TryResolvePath(dirLower, out var id, out var childPrefixLower) || childPrefixLower.Length > 0)
            return;
        if (!index.TryGetIndexById(id, out var rootIdx) || !index.IsDirectory(rootIdx))
            return;

        var stack = new Stack<int>();
        stack.Push(rootIdx);
        var scanned = 0;
        while (stack.Count > 0 && scanned < MaxScannedPerDirectory)
        {
            var current = stack.Pop();
            if (!index.TryGetChildren(current, out var children) || children == null)
                continue;

            foreach (var child in children)
            {
                if (index.IsDeleted(child))
                    continue;

                scanned++;
                if (index.IsDirectory(child))
                    stack.Push(child);

                var modifiedUtc = index.GetLastWriteTimeUnixSeconds(child);
                if (modifiedUtc < cutoffUtc)
                    continue;

                candidates.Add(new SearchResult
                {
                    Name = index.GetName(child),
                    Path = index.GetFullPath(child),
                    IsDir = index.IsDirectory(child),
                    Drive = drive,
                    ModifiedUtc = modifiedUtc
                });
            }
        }
    }
}
