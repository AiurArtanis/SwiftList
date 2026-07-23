using SwiftList.Core.IndexV2;
using SwiftList.Core.Indexer.NetworkDrive;

using SwiftList.Core.IndexV2.Delta;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerExtensions
{
    // Reasons that mean the file's data/attributes changed in a way that can affect Size or the three
    // tracked timestamps. None of these carry the actual values on the USN record itself, so handling
    // them means an extra re-stat, unlike the name-index reasons above which the record already covers.
    // FILE_CREATE and RENAME_NEW_NAME are included too: a fresh link always lands with Size/timestamps
    // defaulted to zero, whether or not a sibling row for the same FRN already had real stat data -- a
    // create-and-immediately-write burst or a rename-with-attribute-change happens to carry a
    // DATA_*/BASIC_INFO_CHANGE reason in the same record and self-corrects, but a file created and left
    // empty, or a plain rename/move with no other change, doesn't. Left unhandled, that file's Size and
    // Creation/LastWrite/LastAccess time all stay at zero, which GetRecentFiles reads as "created at
    // the Unix epoch" and filters out of every age-windowed query.
    private const uint MetadataRefreshReasons = Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME
        | Win32Api.USN_REASON_DATA_EXTEND | Win32Api.USN_REASON_DATA_OVERWRITE | Win32Api.USN_REASON_DATA_TRUNCATION
        | Win32Api.USN_REASON_BASIC_INFO_CHANGE | Win32Api.USN_REASON_COMPRESSION_CHANGE | Win32Api.USN_REASON_ENCRYPTION_CHANGE;

    public static long CatchUpDrive(this UsnIndexer indexer, string drive, ulong journalId, long startUsn)
    {
        var changes = new List<ParsedUsnRecord>();
        var nextUsn = indexer._reader.CatchUpDrive(drive, journalId, startUsn, changes.Add);
        if (nextUsn >= 0 && changes.Count > 0)
            indexer.ApplyUsnRecords(drive, changes);

        return nextUsn;
    }

    public static void ApplyUsnRecord(this UsnIndexer indexer, string drive, ParsedUsnRecord record)
        => indexer.ApplyUsnRecords(drive, new[] { record });

    public static void ApplyUsnRecords(this UsnIndexer indexer, string drive, IReadOnlyList<ParsedUsnRecord> records)
    {
        Logger.Log($"[UsnIndexer] Applying {records.Count} USN records to drive {drive}", LogLevel.Debug);

        LiveIndex? live;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out live))
                return;
        }

        var namePool = new FileRecordNamePool();
        var pendingMetadataFrns = new HashSet<UInt128>();

        // One Mutate call for the whole batch -- LiveIndex's write lock makes the batch atomic with
        // respect to concurrent searches on this drive (a search never sees half the batch applied).
        live.Mutate((snapshot, delta) =>
        {
            foreach (var record in records)
            {
                // One-to-many: operate on the exact link the record names (FRN, parent, name), so
                // renaming/deleting/creating one hard link never disturbs the file's other links.
                var frn = record.FileReferenceNumber;
                var parentFrn = record.ParentFileReferenceNumber;
                var linkName = namePool.Get(record.FileName);
                var linkFlags = FileRecordFlagsHelper.FromAttributes((FileAttributes)record.FileAttributes);

                if ((record.Reason & Win32Api.USN_REASON_HARD_LINK_CHANGE) != 0
                    && (record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_FILE_DELETE)) == 0)
                {
                    DeltaLinkOps.ToggleLink(delta, frn, parentFrn, linkName, linkFlags);
                }
                else if ((record.Reason & Win32Api.USN_REASON_RENAME_OLD_NAME) != 0)
                {
                    // Unlike a real delete, the FRN survives under a new name (RENAME_NEW_NAME follows),
                    // so a directory's children must not cascade-remove here.
                    DeltaLinkOps.RemoveLinkForRename(delta, frn, parentFrn, linkName);
                }
                else if ((record.Reason & Win32Api.USN_REASON_FILE_DELETE) != 0)
                {
                    DeltaLinkOps.RemoveLink(delta, frn, parentFrn, linkName);
                }
                else if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) != 0)
                {
                    DeltaLinkOps.AddLink(delta, frn, parentFrn, linkName, linkFlags);
                }

                // Unlike name-index changes, Size/timestamps are never carried by the USN record itself
                // (USN_RECORD has no such fields), so any of these reasons -- including a plain create --
                // needs an actual re-stat. Just collect which FRNs need it here; the actual I/O happens
                // after this call returns.
                if ((record.Reason & MetadataRefreshReasons) != 0 && (record.Reason & Win32Api.USN_REASON_FILE_DELETE) == 0)
                    pendingMetadataFrns.Add(frn);
            }
        });

        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
        }
        SearchCoordinator.ClearCaches();

        // Stat outside any lock: a write-heavy burst (build, bulk copy) can touch hundreds of distinct
        // files in one 64KB journal buffer, and holding a lock for that many disk stats would serialize
        // this drive's searches/updates behind the whole batch.
        if (pendingMetadataFrns.Count > 0)
            RefreshMetadata(live, pendingMetadataFrns);

        indexer.PublishStatusChanged();
    }

    private static void RefreshMetadata(LiveIndex live, HashSet<UInt128> frns)
    {
        // Path lookups need the read lock (DeltaOverlay's dictionaries aren't safe to read without it,
        // unlike the old engine's bespoke concurrent collections) -- but that's cheap; the disk I/O
        // below runs with no lock held at all.
        var paths = live.Read((snapshot, delta) =>
        {
            var map = new Dictionary<UInt128, string>(frns.Count);
            foreach (var frn in frns)
                if (delta.TryGetPathForFrn(frn, out var path))
                    map[frn] = path;
            return map;
        });

        var results = new List<(UInt128 Frn, long Size, uint CreationTimeUtc, uint LastWriteTimeUtc, uint LastAccessTimeUtc)>(paths.Count);
        foreach (var (frn, path) in paths)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                    continue;
            }
            catch
            {
                continue;
            }

            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            results.Add((frn, isDirectory ? 0 : info.Length,
                FileTimeHelper.ToUnixSeconds(info.CreationTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastWriteTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastAccessTimeUtc)));
        }

        if (results.Count == 0)
            return;

        live.Mutate((snapshot, delta) =>
        {
            foreach (var result in results)
                DeltaLinkOps.UpdateMetadata(delta, result.Frn, result.Size, result.CreationTimeUtc, result.LastWriteTimeUtc, result.LastAccessTimeUtc);
        });
    }

    public static void ApplyFolderChange(this UsnIndexer indexer, string drive, WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        LiveIndex? live;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out live))
                return;
        }

        var root = $"{drive}:\\";
        var normalizedPath = PathHelpers.NormalizePath(path, Directory.Exists(path));
        var changed = false;

        live.Mutate((snapshot, delta) => changed = changeType switch
        {
            WatcherChangeTypes.Deleted => DeltaPathApplier.ApplyDeleted(delta, normalizedPath),
            WatcherChangeTypes.Renamed when !string.IsNullOrWhiteSpace(oldPath) => DeltaPathApplier.ApplyRenamed(delta, (UInt128)1, root, oldPath, normalizedPath),
            _ => DeltaPathApplier.ApplyCreatedOrChanged(delta, (UInt128)1, root, normalizedPath),
        });
        if (!changed)
            return;

        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
        }
        SearchCoordinator.ClearCaches();
        indexer.SaveDriveSnapshot(drive, live);
        indexer.PublishStatusChanged();
    }

    public static void SaveDriveSnapshot(this UsnIndexer indexer, string drive, LiveIndex live)
    {
        if (!indexer._driveMetadata.TryGetValue(drive, out var metadata))
            return;

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        live.Compact(LocalDriveCacheLocator.GetCachePath(cacheDir, drive), new CompactionStamp(metadata.JournalId, metadata.NextUsn), force: true);
    }
}
