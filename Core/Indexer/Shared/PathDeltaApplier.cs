using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Shared;

internal static class PathDeltaApplier
{
    public static bool ApplyCreatedOrChanged(RuntimeIndex runtime, UInt128 rootId, string root, string path, ExclusionRuleSet? exclusionRules = null) => UpsertPath(runtime, rootId, root, path, includeChildren: Directory.Exists(path), exclusionRules);

    public static bool ApplyDeleted(RuntimeIndex runtime, string path)
    {
        var filePath = PathHelpers.NormalizePath(path, isDirectory: false);
        var removed = RemoveSubtree(runtime, (UInt128)PathHelpers.HashPath64(filePath));
        var directoryPath = PathHelpers.NormalizePath(path, isDirectory: true);
        if (!directoryPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            removed |= RemoveSubtree(runtime, (UInt128)PathHelpers.HashPath64(directoryPath));
        return removed;
    }

    public static bool ApplyRenamed(RuntimeIndex runtime, UInt128 rootId, string root, string oldPath, string newPath, ExclusionRuleSet? exclusionRules = null)
    {
        var changed = ApplyDeleted(runtime, oldPath);
        changed |= UpsertPath(runtime, rootId, root, newPath, includeChildren: Directory.Exists(newPath), exclusionRules);
        return changed;
    }

    private static bool UpsertPath(RuntimeIndex runtime, UInt128 rootId, string root, string path, bool includeChildren, ExclusionRuleSet? exclusionRules)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (exclusionRules?.IsExcludedPath(path, isDirectory) == true)
            return ApplyDeleted(runtime, path);

        var normalized = PathHelpers.NormalizePath(path, isDirectory);
        var normalizedRoot = PathHelpers.NormalizePath(root, isDirectory: true);
        if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        EnsureParentChain(runtime, rootId, normalizedRoot, normalized);

        var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var parentPath = Path.GetDirectoryName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parentId = string.IsNullOrWhiteSpace(parentPath) || PathHelpers.NormalizePath(parentPath, true).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? rootId
            : (UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(parentPath, true));

        runtime.Upsert(new FileRecord(
            (UInt128)PathHelpers.HashPath64(normalized),
            parentId,
            name,
            isDirectory ? FileRecordFlags.Directory : FileRecordFlags.None));

        if (includeChildren && isDirectory)
            UpsertDirectoryChildren(runtime, rootId, root, normalized, exclusionRules);

        return true;
    }

    private static void UpsertDirectoryChildren(RuntimeIndex runtime, UInt128 rootId, string root, string directory, ExclusionRuleSet? exclusionRules)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory);
        }
        catch
        {
            return;
        }

        foreach (var child in children)
            UpsertPath(runtime, rootId, root, child, includeChildren: true, exclusionRules);
    }

    private static void EnsureParentChain(RuntimeIndex runtime, UInt128 rootId, string normalizedRoot, string normalizedPath)
    {
        var parentPath = Path.GetDirectoryName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath))
            return;

        var normalizedParent = PathHelpers.NormalizePath(parentPath, isDirectory: true);
        if (normalizedParent.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var parentId = (UInt128)PathHelpers.HashPath64(normalizedParent);
        if (runtime.TryGetIndexById(parentId, out _))
            return;

        EnsureParentChain(runtime, rootId, normalizedRoot, normalizedParent);

        var parentParentPath = Path.GetDirectoryName(normalizedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parentParentId = string.IsNullOrWhiteSpace(parentParentPath) || PathHelpers.NormalizePath(parentParentPath, true).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? rootId
            : (UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(parentParentPath, true));

        var parentName = Path.GetFileName(normalizedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentName))
            return;

        runtime.Upsert(new FileRecord(
            parentId,
            parentParentId,
            parentName,
            FileRecordFlags.Directory));
    }

    private static bool RemoveSubtree(RuntimeIndex runtime, UInt128 id)
    {
        if (!runtime.TryGetIndexById(id, out var idx))
            return false;

        var removed = false;
        var stack = new Stack<int>();
        stack.Push(idx);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (runtime.TryGetChildren(current, out var children) && children != null)
            {
                foreach (var child in children)
                    stack.Push(child);
            }
            runtime.Remove(runtime.GetId(current));
            removed = true;
        }

        return removed;
    }
}
