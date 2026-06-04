using System;
using System.IO;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal static class PathHelpers
    {
        public static string NormalizePath(string path, bool isDirectory)
        {
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (isDirectory && !normalized.EndsWith(Path.DirectorySeparatorChar))
                normalized += Path.DirectorySeparatorChar;
            return normalized;
        }

        public static UInt128 HashPath(string path)
        {
            string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .ToLowerInvariant();

            ulong low = 14695981039346656037UL;
            ulong high = 1099511628211UL;
            foreach (char c in normalized)
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
            UInt128 hash = HashPath(path);
            ulong low = (ulong)hash;
            ulong high = (ulong)(hash >> 64);
            ulong value = low ^ high;
            return value == 0 ? 1 : value;
        }
    }
}
