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
