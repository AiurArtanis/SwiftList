using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core.Indexer.Usn;

internal static class FolderDriveScanner
{
    public static FileRecordStore Build(string drive, Action<int, int>? onProgress, CancellationToken token)
    {
        var root = $"{drive}:\\";
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.SourceLocalId64,
            FileSystemType = identity?.FileSystemType ?? string.Empty,
            VolumeSerialNumber = identity?.SerialNumber ?? 0,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        var settings = UserSettings.Load();
        var filter = WalkFilter.Create(root, new WalkOptions(
            settings.ExcludedPaths,
            settings.IgnoredPathGlobs,
            settings.IgnoredPathRegexes,
            0,
            0,
            true));
        var files = 0;
        var dirs = 0;
        Walk(root, root, 1, 0, NetworkIgnoreRuleSet.Empty, filter, store, ref files, ref dirs, onProgress, token);
        onProgress?.Invoke(files, dirs);
        return store;
    }

    private static void Walk(string dir, string logicalDir, UInt128 parentId, int depth, NetworkIgnoreRuleSet inheritedRules, WalkFilter filter, FileRecordStore store, ref int files, ref int dirs, Action<int, int>? onProgress, CancellationToken token)
    {
        var ignoreRules = filter.LoadIgnoreRules(dir, logicalDir, inheritedRules);
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch { return; }

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            FileAttributes attrs;
            try { attrs = File.GetAttributes(entry); }
            catch { continue; }

            if ((attrs & FileAttributes.ReparsePoint) != 0)
                continue;

            var isDir = (attrs & FileAttributes.Directory) != 0;
            var logicalPath = PathHelpers.NormalizePath(entry, isDir);
            var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!filter.ShouldIndex(logicalPath, name, isDir, attrs, ignoreRules))
                continue;

            var id = (UInt128)PathHelpers.HashPath64(logicalPath);
            store.Records.Add(new FileRecord(id, parentId, name, isDir ? FileRecordFlags.Directory : FileRecordFlags.None));
            if (isDir) dirs++;
            else files++;
            if (((files + dirs) & 4095) == 0)
                onProgress?.Invoke(files, dirs);
            if (isDir && filter.ShouldDescend(logicalPath, attrs, depth + 1, ignoreRules))
                Walk(entry, logicalPath, id, depth + 1, ignoreRules, filter, store, ref files, ref dirs, onProgress, token);
        }
    }
}
