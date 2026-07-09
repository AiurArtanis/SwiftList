using System.Text;

namespace SwiftList.Core;

// Reads just the .meta header of a FileRecordStoreSerializer-produced cache (no .records/.names
// parsing) -- kept as its own static class (not a partial split of FileRecordStoreSerializer) to stay
// under the per-file line limit.
public static class FileRecordStoreSummaryLoader
{
    public static FileRecordStoreSummary? LoadSummary(string cacheDir, string sourceKey)
    {
        var basePath = FileRecordStoreSerializer.GetBasePath(cacheDir, sourceKey);
        try
        {
            if (!FileRecordStoreSerializer.Exists(cacheDir, sourceKey))
                return null;

            using var meta = File.OpenRead(basePath + ".meta");
            using var reader = new BinaryReader(meta, Encoding.UTF8);
            if (reader.ReadString() != FileRecordStoreSerializer.MetaMagic || reader.ReadInt32() != FileRecordStoreSerializer.Version)
                return null;

            var storeSourceKey = reader.ReadString();
            var sourceKind = (FileRecordSourceKind)reader.ReadByte();
            var idKind = (FileRecordIdKind)reader.ReadByte();
            var fileSystemType = reader.ReadString();
            var volumeSerialNumber = reader.ReadUInt32();
            var rootLow = reader.ReadUInt64();
            var rootHigh = reader.ReadUInt64();
            var journalId = reader.ReadUInt64();
            var nextUsn = reader.ReadInt64();
            var recordCount = reader.ReadInt32();
            var liveRecordCount = reader.ReadInt32();
            var ticks = reader.ReadInt64();
            var rootId = new UInt128(rootHigh, rootLow);
            var lastUpdated = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            return new FileRecordStoreSummary(
                storeSourceKey,
                sourceKind,
                idKind,
                fileSystemType,
                volumeSerialNumber,
                rootId,
                journalId,
                nextUsn,
                recordCount,
                liveRecordCount,
                lastUpdated);
        }
        catch (IOException)
        {
            // A concurrent Save() (e.g. a checkpoint mid-scan) can have this exact .meta file open for its
            // temp-file swap right now -- purely transient, not evidence the cache is corrupted. Propagate
            // instead of swallowing into null, so callers that treat a null summary as "bad, delete it"
            // (NetworkDriveCacheLocator.EnumerateNetworkStores) don't wipe out a cache that's mid-write.
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileRecordStoreSummaryLoader] Failed to load summary {basePath}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
