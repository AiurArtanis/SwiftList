using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

internal static class IndexCacheManager
{
    public static FileRecordStore CreateEmptyStore(
        string drive,
        UInt128 rootFrn,
        long nextUsn,
        ulong journalId)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = rootFrn,
            JournalId = journalId,
            NextUsn = nextUsn
        };
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        if (identity.HasValue)
        {
            store.FileSystemType = identity.Value.FileSystemType;
            store.VolumeSerialNumber = identity.Value.SerialNumber;
        }

        store.Records.Add(new FileRecord(
            store.RootId,
            store.RootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        return store;
    }

    public static FileRecordStore CreateStoreFromDriveData(
        string drive,
        UInt128 rootFrn,
        Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)> searchItems,
        long nextUsn,
        ulong journalId)
    {
        var store = CreateEmptyStore(drive, rootFrn, nextUsn, journalId);
        var namePool = new FileRecordNamePool();

        foreach (var kvp in searchItems)
        {
            var flags = kvp.Value.IsDir ? FileRecordFlags.Directory : FileRecordFlags.None;
            store.Records.Add(new FileRecord(
                kvp.Key,
                kvp.Value.ParentFrn,
                namePool.Get(kvp.Value.Name),
                flags));
        }

        return store;
    }

    public static void SaveDrivesToCache(
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> driveMetadata,
        IReadOnlyDictionary<string, RuntimeIndex> recordIndexes,
        IReadOnlyDictionary<string, UsnIndexer.DriveRuntimeMetadata> driveMetadataMap)
    {
        foreach (var meta in driveMetadata)
        {
            if (!driveMetadataMap.TryGetValue(meta.Drive, out var metadata))
                continue;

            if (recordIndexes.TryGetValue(meta.Drive, out var runtime))
            {
                metadata.JournalId = meta.JournalId;
                metadata.NextUsn = meta.NextUsn;
                var store = runtime.ToStore(
                    metadata.SourceKind,
                    metadata.IdKind,
                    metadata.FileSystemType,
                    metadata.VolumeSerialNumber,
                    metadata.RootId,
                    metadata.JournalId,
                    metadata.NextUsn);
                LocalDriveCacheLocator.Save(cacheDir, meta.Drive, store);
            }
        }
    }
}
