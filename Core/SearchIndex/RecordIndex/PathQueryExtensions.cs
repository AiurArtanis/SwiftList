namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class PathQueryExtensions
{
    public static bool TryResolvePath(this RuntimeIndex index, string pathLower, out UInt128 id, out string childPrefixLower, bool forceLastSegmentAsQuery = false)
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

            if (isLast && forceLastSegmentAsQuery)
            {
                childPrefixLower = segment;
                id = index.Ids[current];
                return true;
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
        if (index.PathMemo.TryGetValue(id, out var path))
            return path;

        var parentIndex = index.ParentIndexes[idx];
        var name = index.GetName(idx);

        if (parentIndex < 0 || parentIndex == idx)
            path = Path.Combine(index.SourceRoot, name);
        else
            path = Path.Combine(index.GetFullPath(parentIndex), name);

        index.PathMemo.TryAdd(id, path);
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
