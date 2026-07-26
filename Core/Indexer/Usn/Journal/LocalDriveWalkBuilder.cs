using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.Indexer.NetworkDrive.Walk;

namespace SwiftList.Core.Indexer.Usn.Journal;

// Replaces FolderDriveScanner: builds a non-journal (FAT32/exFAT/etc.) local drive's FileRecordStore by
// calling straight into the same TreeBuilder/TreeDiffBaseline/checkpoint machinery network/WSL/folder-index
// drives use (Core/Indexer/NetworkDrive/Walk/*), instead of a hand-rolled single-threaded recursive walk
// with no incremental reuse and no checkpoint/resume. That machinery is already fully generic local-
// filesystem code with no network/UNC/WSL coupling in the traversal itself -- calling it directly from this
// (elevated-service-process) namespace needs no porting, just an orchestrator modeled on NetworkIndex.Build.
//
// Deliberately does NOT apply exclusion/ignore rules (WalkOptions is always empty/no-op below) -- local
// drives never have, and this refactor isn't the place to introduce that as a new behavior change.
internal static class LocalDriveWalkBuilder
{
    private static readonly WalkOptions NoFiltering = new(
        ExcludedPaths: Array.Empty<string>(),
        IgnoredPathGlobs: Array.Empty<string>(),
        IgnoredPathRegexes: Array.Empty<string>(),
        MaxDepth: 0,
        WorkerCount: 0,
        UseIgnoreFiles: false);

    // `root` is taken as an explicit parameter (not derived from `drive` internally) so this can be pointed
    // at a real temp directory in tests, mirroring NetworkIndex.Build's own decoupled drive/root/physicalRoot
    // parameters -- the production caller just passes "{drive}:\\" for both `root` and `drive`'s volume-
    // identity lookup, same effective behavior FolderDriveScanner had.
    public static FileRecordStore Build(
        string drive,
        string root,
        FileRecordStore? previousStore,
        Action<int, int> onProgress,
        CancellationToken token,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null)
    {
        const ulong rootId = 1;
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.SourceLocalId64,
            FileSystemType = identity?.FileSystemType ?? string.Empty,
            VolumeSerialNumber = identity?.SerialNumber ?? 0,
            RootId = rootId,
        };

        // Real root mtime, the same as TryCreateRecord stats every other directory -- without it
        // TreeDiffBaseline could never match this record against a live stat, permanently forcing every
        // top-level entry to be re-listed on every resume no matter how unchanged they actually are.
        uint rootLastWriteTime = 0;
        try { rootLastWriteTime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(root)); } catch { }

        store.Records.Add(new FileRecord(
            rootId,
            rootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot,
            lastWriteTimeUnixSeconds: rootLastWriteTime));

        var diffBaseline = TreeDiffBaseline.From(previousStore);
        var builder = new TreeBuilder(store, root, root, NoFiltering, token, onProgress, onCheckpoint, diffBaseline, recheckExclusions: false);
        builder.Run();

        store.IsComplete = true;
        return store;
    }
}
