namespace SwiftList.Core.Indexer.NetworkDrive;

internal static class PathHelpers
{
    public static string NormalizePath(string path, bool isDirectory)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (isDirectory && !normalized.EndsWith(Path.DirectorySeparatorChar))
            normalized += Path.DirectorySeparatorChar;
        return normalized;
    }

    // A bare drive letter ("Z") needs ":\" appended to form a root. A UNC path just needs a trailing
    // separator. Anything else (a folder-index target, e.g. "Z:\AV") is already a full path -- appending
    // ":\" there would produce "Z:\AV:\", a colon in the middle of the path that can never resolve.
    // Mirrors RuntimeIndex.ComputeSourceRoot (same three cases, different owner -- WatcherManager needs
    // this to translate a raw drive key back into a root before it can diff a watcher event against it).
    public static string BuildSourceRoot(string sourceKey) =>
        sourceKey.StartsWith(@"\\") || sourceKey.StartsWith(@"//") ? (sourceKey.EndsWith(@"\") ? sourceKey : sourceKey + @"\")
        : sourceKey.Length == 1 ? sourceKey + @":\"
        : sourceKey.EndsWith(@"\") ? sourceKey : sourceKey + @"\";

    public static UInt128 HashPath(string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var low = 14695981039346656037UL;
        var high = 1099511628211UL;
        foreach (var c in normalized)
        {
            low ^= c;
            low *= 1099511628211UL;
            high ^= (uint)c + 0x9E3779B97F4A7C15UL;
            high *= 14029467366897019727UL;
        }

        return new UInt128(high, low);
    }

    public static ulong HashPath64(string path)
    {
        var hash = HashPath(path);
        var low = (ulong)hash;
        var high = (ulong)(hash >> 64);
        var value = low ^ high;
        return value == 0 ? 1 : value;
    }
}
