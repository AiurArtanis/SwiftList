using SwiftList.Core.Indexer.Shared;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerExtensions
{
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
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out var runtime))
                return;
            var namePool = new FileRecordNamePool();

            foreach (var record in records)
            {
                if (Mft.MftHardLinkOptions.Enabled)
                {
                    // Hard link added/removed (not last): re-diff this FRN's on-disk links against
                    // the index. The diff also covers remove-to-empty if a HARD_LINK_CHANGE arrives
                    // for the last link.
                    if ((record.Reason & Win32Api.USN_REASON_HARD_LINK_CHANGE) != 0
                        && (record.Reason & Win32Api.USN_REASON_FILE_CREATE) == 0)
                    {
                        HardLinkDelta.ApplyDiff(runtime, drive, record.FileReferenceNumber);
                        continue;
                    }
                    // File deleted (last link gone): drop every row for this FRN, not just one.
                    if ((record.Reason & Win32Api.USN_REASON_FILE_DELETE) != 0
                        && (record.Reason & Win32Api.USN_REASON_RENAME_OLD_NAME) == 0)
                    {
                        foreach (var row in runtime.RowsForFrn(record.FileReferenceNumber))
                            runtime.MarkRowDeleted(row);
                        runtime.Remove(record.FileReferenceNumber);
                        continue;
                    }
                    // create / rename fall through to the one-to-one path below (single-link common
                    // case); extra links show up as HARD_LINK_CHANGE and go through the diff above.
                }

                if ((record.Reason & (Win32Api.USN_REASON_FILE_DELETE | Win32Api.USN_REASON_RENAME_OLD_NAME)) != 0)
                {
                    runtime.Remove(record.FileReferenceNumber);
                    continue;
                }

                // A hard link was added or removed. The reason alone can't say which, and the file
                // may still have other links, so re-resolve the FRN to a currently-valid name
                // (or remove it if the last link is gone) instead of trusting this record's name.
                if ((record.Reason & Win32Api.USN_REASON_HARD_LINK_CHANGE) != 0
                    && (record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_FILE_DELETE)) == 0)
                {
                    if (HardLinkResolver.TryResolveRecord(drive, record.FileReferenceNumber, out var current))
                    {
                        var linkFlags = FileRecordFlagsHelper.FromAttributes((FileAttributes)current.FileAttributes);
                        runtime.Upsert(new FileRecord(
                            current.FileReferenceNumber,
                            current.ParentFileReferenceNumber,
                            namePool.Get(current.FileName),
                            linkFlags));
                    }
                    else
                    {
                        runtime.Remove(record.FileReferenceNumber);
                    }
                    continue;
                }

                if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) == 0)
                    continue;

                var flags = FileRecordFlagsHelper.FromAttributes((FileAttributes)record.FileAttributes);
                var fileRecord = new FileRecord(
                    record.FileReferenceNumber,
                    record.ParentFileReferenceNumber,
                    namePool.Get(record.FileName),
                    flags);

                runtime.Upsert(fileRecord);
            }

            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
            SearchCoordinator.ClearCaches();
        }
        indexer.PublishStatusChanged();
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
