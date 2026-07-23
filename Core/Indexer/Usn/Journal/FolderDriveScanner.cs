using SwiftList.Core.Indexer.NetworkDrive;

using SwiftList.Core.Indexer.NetworkDrive.Walk;
namespace SwiftList.Core.Indexer.Usn.Journal;

internal static class FolderDriveScanner
{
    internal readonly record struct FolderDriveBuildResult(FileRecordStore Store, string FileSystemType, uint VolumeSerialNumber, UInt128 RootId);

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
            RootId = 1,
            // No partial-checkpoint concept for this fallback walk -- it's always a full build.
            IsComplete = true
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        var files = 0;
        var dirs = 0;
        Walk(root, 1, store, ref files, ref dirs, onProgress, token);
        onProgress?.Invoke(files, dirs);
        return store;
    }

    public static FolderDriveBuildResult? BuildStreaming(string drive, Action<int, int>? onProgress, CancellationToken token)
    {
        var store = Build(drive, onProgress, token);
        return new FolderDriveBuildResult(store, store.FileSystemType, store.VolumeSerialNumber, store.RootId);
    }

    private static void Walk(string dir, UInt128 parentId, FileRecordStore store, ref int files, ref int dirs, Action<int, int>? onProgress, CancellationToken token)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch { return; }

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            FileInfo info;
            FileAttributes attrs;
            try
            {
                info = new FileInfo(entry);
                attrs = info.Attributes;
            }
            catch { continue; }

            if ((attrs & FileAttributes.ReparsePoint) != 0)
                continue;

            var isDir = (attrs & FileAttributes.Directory) != 0;
            var logicalPath = PathHelpers.NormalizePath(entry, isDir);
            var name = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var id = (UInt128)PathHelpers.HashPath64(logicalPath);
            var flags = FileRecordFlagsHelper.FromAttributes(attrs);
            var size = isDir ? 0 : info.Length;
            store.Records.Add(new FileRecord(id, parentId, name, flags, size,
                FileTimeHelper.ToUnixSeconds(info.CreationTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastWriteTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastAccessTimeUtc)));
            if (isDir) dirs++;
            else files++;
            if (((files + dirs) & 4095) == 0)
                onProgress?.Invoke(files, dirs);
            if (isDir)
                Walk(entry, id, store, ref files, ref dirs, onProgress, token);
        }
    }
}
