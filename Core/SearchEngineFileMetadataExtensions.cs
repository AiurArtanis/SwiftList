using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core;

// Backs the GetFileMetadata pipe request: looks up Size/Created/Modified/Accessed straight out of
// the in-memory index (no disk I/O) for whichever of the given paths are actually indexed. Paths
// that aren't found (not yet scanned, on an unindexed drive, etc) are simply omitted -- the client
// falls back to a live filesystem stat for those.
public static class SearchEngineFileMetadataExtensions
{
    public static Dictionary<string, FileMetadataEntry> GetFileMetadataBatch(this UsnIndexer indexer, IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        lock (indexer.LockObj)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || !char.IsLetter(root[0]))
                    continue;

                var drive = root[0].ToString().ToUpperInvariant();
                if (!indexer._recordIndexes.TryGetValue(drive, out var index))
                    continue;

                var pathLower = path.ToLowerInvariant();
                if (!index.TryResolvePath(pathLower, out var id, out var childPrefixLower) || childPrefixLower.Length > 0)
                    continue;
                if (!index.TryGetIndexById(id, out var idx))
                    continue;

                result[path] = new FileMetadataEntry(
                    index.GetSize(idx),
                    index.GetCreationTimeUnixSeconds(idx),
                    index.GetLastWriteTimeUnixSeconds(idx),
                    index.GetLastAccessTimeUnixSeconds(idx));
            }
        }
        return result;
    }
}
