using SwiftList.Core.Indexer.Shared;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerExtensions
{
    // Reasons that mean the file's data/attributes changed in a way that can affect Size or the three
    // tracked timestamps. None of these carry the actual values on the USN record itself, so handling
    // them means an extra re-stat, unlike the name-index reasons above which the record already covers.
    private const uint MetadataRefreshReasons = Win32Api.USN_REASON_DATA_EXTEND | Win32Api.USN_REASON_DATA_OVERWRITE
        | Win32Api.USN_REASON_DATA_TRUNCATION | Win32Api.USN_REASON_BASIC_INFO_CHANGE
        | Win32Api.USN_REASON_COMPRESSION_CHANGE | Win32Api.USN_REASON_ENCRYPTION_CHANGE;

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
        RuntimeIndex? runtime;
        HashSet<UInt128> pendingMetadataFrns;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out runtime))
                return;
            var namePool = new FileRecordNamePool();
            pendingMetadataFrns = new HashSet<UInt128>();

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
                    HardLinkDelta.ToggleLink(runtime, frn, parentFrn, linkName, linkFlags);
                }
                else if ((record.Reason & Win32Api.USN_REASON_RENAME_OLD_NAME) != 0)
                {
                    // Unlike a real delete, the FRN survives under a new name (RENAME_NEW_NAME follows),
                    // so a directory's children must not be cascade-removed here.
                    HardLinkDelta.RemoveLinkForRename(runtime, frn, parentFrn, linkName);
                }
                else if ((record.Reason & Win32Api.USN_REASON_FILE_DELETE) != 0)
                {
                    HardLinkDelta.RemoveLink(runtime, frn, parentFrn, linkName);
                }
                else if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) != 0)
                {
                    HardLinkDelta.AddLink(runtime, frn, parentFrn, linkName, linkFlags);
                }

                // Unlike name-index changes, Size/timestamps are never carried by the USN record itself
                // (USN_RECORD has no such fields), so a content/attribute-only change needs an actual
                // re-stat -- including right after FILE_CREATE, since a create-and-immediately-write burst
                // arrives as one record with both reasons set and AddLink alone leaves them at zero. Just
                // collect which FRNs need it here; the actual I/O happens after this lock is released.
                if ((record.Reason & MetadataRefreshReasons) != 0 && (record.Reason & Win32Api.USN_REASON_FILE_DELETE) == 0)
                    pendingMetadataFrns.Add(frn);
            }

            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
            SearchCoordinator.ClearCaches();
        }

        // Stat outside the index lock: a write-heavy burst (build, bulk copy) can touch hundreds of
        // distinct files in one 64KB journal buffer, and LockObj is shared across every drive plus
        // search -- holding it for that many disk stats would serialize all of that behind this batch.
        if (pendingMetadataFrns.Count > 0)
            RefreshMetadata(indexer, runtime, pendingMetadataFrns);

        indexer.PublishStatusChanged();
    }

    // RowsForFrn/GetFullPath are safe to call without the lock -- the rest of the codebase already reads
    // them lock-free from the search path while USN updates run concurrently. Only the final column write
    // (cheap, no I/O) needs the lock, and it re-resolves each FRN's rows there rather than trusting this
    // snapshot, since a later record for the same FRN could have renamed/deleted it in the meantime.
    private static void RefreshMetadata(UsnIndexer indexer, RuntimeIndex runtime, HashSet<UInt128> frns)
    {
        var results = new List<(UInt128 Frn, long Size, long CreationTimeUtc, long LastWriteTimeUtc, long LastAccessTimeUtc)>(frns.Count);
        foreach (var frn in frns)
        {
            // Hard links share one $STANDARD_INFORMATION (and $DATA stream), so a single stat via any
            // one link's path is authoritative for every row of this FRN.
            var rows = runtime.RowsForFrn(frn);
            if (rows.Count == 0)
                continue;

            FileInfo info;
            try
            {
                info = new FileInfo(runtime.GetFullPath(rows[0]));
                if (!info.Exists)
                    continue;
            }
            catch
            {
                continue;
            }

            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            results.Add((frn, isDirectory ? 0 : info.Length,
                info.CreationTimeUtc.ToFileTimeUtc(), info.LastWriteTimeUtc.ToFileTimeUtc(), info.LastAccessTimeUtc.ToFileTimeUtc()));
        }

        if (results.Count == 0)
            return;

        lock (indexer.LockObj)
        {
            foreach (var result in results)
                foreach (var row in runtime.RowsForFrn(result.Frn))
                    runtime.UpdateMetadata(row, result.Size, result.CreationTimeUtc, result.LastWriteTimeUtc, result.LastAccessTimeUtc);
        }
    }

    public static void ApplyFolderChange(this UsnIndexer indexer, string drive, WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out var runtime))
                return;

            var root = $"{drive}:\\";
            var normalizedPath = PathHelpers.NormalizePath(path, Directory.Exists(path));
            var changed = false;

            changed = changeType switch
            {
                WatcherChangeTypes.Deleted => PathDeltaApplier.ApplyDeleted(runtime, normalizedPath),
                WatcherChangeTypes.Renamed when !string.IsNullOrWhiteSpace(oldPath) => PathDeltaApplier.ApplyRenamed(runtime, (UInt128)1, root, oldPath, normalizedPath),
                _ => PathDeltaApplier.ApplyCreatedOrChanged(runtime, (UInt128)1, root, normalizedPath),
            };
            if (!changed)
                return;

            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
            SearchCoordinator.ClearCaches();
            indexer.SaveDriveSnapshot(drive, runtime);
        }
        indexer.PublishStatusChanged();
    }

    public static void SaveDriveSnapshot(this UsnIndexer indexer, string drive, RuntimeIndex runtime)
    {
        if (!indexer._driveMetadata.TryGetValue(drive, out var metadata))
            return;

        var store = runtime.ToStore(
            metadata.SourceKind,
            metadata.IdKind,
            metadata.FileSystemType,
            metadata.VolumeSerialNumber,
            metadata.RootId,
            metadata.JournalId,
            metadata.NextUsn);
        LocalDriveCacheLocator.Save(Path.Combine(Logger.UserDataDir, "indexes"), drive, store);
    }
}
