namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class QueryExtensions
{
    public static UInt128 GetId(this RuntimeIndex index, int i) => index.Ids[i];

    public static UInt128 GetParentId(this RuntimeIndex index, int i)
    {
        var parentIndex = index.ParentIndexes[i];
        return (uint)parentIndex < (uint)index.Ids.Count ? index.Ids[parentIndex] : index.Ids[i];
    }

    public static string GetName(this RuntimeIndex index, int i) => index.Names.GetValue(index.NameIds[i]);

    public static FileRecordFlags GetFlags(this RuntimeIndex index, int i) => (FileRecordFlags)index.Flags[i];

    public static bool IsDirectory(this RuntimeIndex index, int i) => (((FileRecordFlags)index.Flags[i]) & FileRecordFlags.Directory) != 0;

    public static bool IsDeleted(this RuntimeIndex index, int i) => (((FileRecordFlags)index.Flags[i]) & FileRecordFlags.Deleted) != 0;

    public static bool HasAlias(this RuntimeIndex index, int i)
    {
        if (i >= index.LoadedCount)
        {
            return index.DeltaNameAliases.TryGetValue(i, out var deltaAliases) && deltaAliases != null && deltaAliases.Length > 0;
        }
        return index.HasAlias.Get(i);
    }

    public static FileRecord GetRecord(this RuntimeIndex index, int i) => new FileRecord(index.Ids[i], index.GetParentId(i), index.GetName(i), (FileRecordFlags)index.Flags[i]);

    public static bool TryGetAliases(this RuntimeIndex index, int i, out string[] aliases)
    {
        if (i >= index.LoadedCount)
        {
            if (index.DeltaNameAliases.TryGetValue(i, out var deltaAliases))
            {
                if (deltaAliases != null && deltaAliases.Length > 0)
                {
                    aliases = deltaAliases;
                    return true;
                }
            }
            aliases = Array.Empty<string>();
            return false;
        }

        if (!index.HasAlias.Get(i))
        {
            aliases = Array.Empty<string>();
            return false;
        }

        if (index.DeltaNameAliases.TryGetValue(i, out var deltaAliasesOverridden))
        {
            if (deltaAliasesOverridden != null && deltaAliasesOverridden.Length > 0)
            {
                aliases = deltaAliasesOverridden;
                return true;
            }
            aliases = Array.Empty<string>();
            return false;
        }

        var pos = Array.BinarySearch(index.AliasIndices, i);
        if (pos >= 0)
        {
            aliases = index.AliasValues[pos];
            return true;
        }

        aliases = Array.Empty<string>();
        return false;
    }

    internal static int ResolveParentIndex(this RuntimeIndex index, UInt128 id, UInt128 parentId) => parentId != id && index.TryGetIndexById(parentId, out var parentIndex) ? parentIndex : -1;

    public static bool TryGetIndexById(this RuntimeIndex index, UInt128 id, out int idx)
    {
        if (index.DeltaIdToIndex.TryGetValue(id, out idx))
        {
            if (!index.IsDeleted(idx))
                return true;

            idx = -1;
            return false;
        }

        if (index.LoadedCount > 0)
        {
            var low = 0;
            var high = index.LoadedCount - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var midId = index.Ids[mid];
                if (midId == id)
                {
                    if (!index.IsDeleted(mid))
                    {
                        idx = mid;
                        return true;
                    }
                    break;
                }
                if (midId < id)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
        }

        idx = -1;
        return false;
    }

    internal static void GetNameCandidateStorage(this RuntimeIndex index, string pattern, out int[]? bucket, out List<int>? delta)
    {
        bucket = null;
        delta = null;
        if (pattern.Length == 0)
            return;

        var bestKey = '\0';
        var minCount = int.MaxValue;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = char.ToLowerInvariant(pattern[i]);
            if (c == ' ' || c == '/' || c == '\\')
                continue;

            var count = 0;
            if (index.NameCharBuckets.TryGetValue(c, out var b))
            {
                count += b.Length;
            }
            if (index.NameCharDelta.TryGetValue(c, out var d))
            {
                count += d.Count;
            }

            if (count < minCount)
            {
                minCount = count;
                bestKey = c;
            }
        }

        if (bestKey != '\0')
        {
            index.NameCharBuckets.TryGetValue(bestKey, out bucket);
            index.NameCharDelta.TryGetValue(bestKey, out delta);
        }
        else
        {
            var key = char.ToLowerInvariant(pattern[0]);
            index.NameCharBuckets.TryGetValue(key, out bucket);
            index.NameCharDelta.TryGetValue(key, out delta);
        }
    }

    public static bool TryResolvePath(this RuntimeIndex index, string pathLower, out UInt128 id, out string childPrefixLower)
    {
        id = default;
        childPrefixLower = string.Empty;

        if (!pathLower.StartsWith(index.SourceRootLower, StringComparison.Ordinal))
            return false;

        if (pathLower.Length == index.SourceRootLower.Length)
        {
            id = index.FindRootId();
            return id != default;
        }

        var current = index.FindRootIndex();
        if (current < 0)
            return false;

        var start = index.SourceRootLower.Length;
        while (start < pathLower.Length)
        {
            var sep = pathLower.IndexOf(Path.DirectorySeparatorChar, start);
            var isLast = sep < 0;
            var segment = isLast ? pathLower.Substring(start) : pathLower.Substring(start, sep - start);
            if (segment.Length == 0)
            {
                start = sep + 1;
                continue;
            }

            if (!index.TryFindChildDirectory(current, segment, out var child))
            {
                childPrefixLower = segment;
                id = index.Ids[current];
                return true;
            }

            current = child;
            if (isLast)
            {
                id = index.Ids[current];
                return true;
            }

            start = sep + 1;
        }

        id = index.Ids[current];
        return true;
    }

    public static string GetFullPath(this RuntimeIndex index, int idx)
    {
        var id = index.Ids[idx];
        var parentIndex = index.ParentIndexes[idx];
        var name = index.GetName(idx);
        if (index.PathMemo.TryGetValue(id, out var path))
            return path;

        if (parentIndex < 0 || parentIndex == idx)
            path = Path.Combine(index.SourceRoot, name);
        else
            path = Path.Combine(index.GetFullPath(parentIndex), name);

        index.PathMemo[id] = path;
        return path;
    }

    public static bool IsUnderDirectory(this RuntimeIndex index, int idx, UInt128 ancestorId)
    {
        if (index.Ids[idx] == ancestorId)
            return true;

        if (!index.TryGetIndexById(ancestorId, out var ancestorIndex))
            return false;

        return index.IsUnderDirectoryIndex(idx, ancestorIndex);
    }

    public static bool IsUnderDirectoryIndex(this RuntimeIndex index, int idx, int ancestorIndex)
    {
        if (idx == ancestorIndex)
            return true;

        var parentIndex = index.ParentIndexes[idx];
        while (parentIndex >= 0)
        {
            if (parentIndex == ancestorIndex)
                return true;

            var grandParentIndex = index.ParentIndexes[parentIndex];
            if (grandParentIndex == parentIndex)
                return false;

            parentIndex = grandParentIndex;
        }

        return false;
    }

    public static void ClearPathCache(this RuntimeIndex index) => index.PathMemo.Clear();

    private static UInt128 FindRootId(this RuntimeIndex index)
    {
        for (var i = 0; i < index.Count; i++)
        {
            if ((((FileRecordFlags)index.Flags[i]) & FileRecordFlags.SourceRoot) != 0)
                return index.Ids[i];
        }

        return default;
    }

    private static int FindRootIndex(this RuntimeIndex index)
    {
        for (var i = 0; i < index.Count; i++)
        {
            if ((((FileRecordFlags)index.Flags[i]) & FileRecordFlags.SourceRoot) != 0)
                return i;
        }

        return -1;
    }

    private static bool TryFindChildDirectory(this RuntimeIndex index, int parentIndex, string nameLower, out int childIndexResult)
    {
        childIndexResult = -1;
        foreach (var childIndex in index.EnumerateChildren(parentIndex))
        {
            if (index.IsDirectory(childIndex) && index.GetName(childIndex).Equals(nameLower, StringComparison.OrdinalIgnoreCase))
            {
                childIndexResult = childIndex;
                return true;
            }
        }

        return false;
    }

    public static ChildEnumerable EnumerateChildren(this RuntimeIndex index, UInt128 parentId) => index.TryGetIndexById(parentId, out var parentIndex)
            ? index.EnumerateChildren(parentIndex)
            : new ChildEnumerable(index, -1);

    public static ChildEnumerable EnumerateChildren(this RuntimeIndex index, int parentIndex) => new ChildEnumerable(index, parentIndex);
}
