using System.Text;

namespace SwiftList.Core;

internal sealed class FileRecordNamePool
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

    public string Get(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        lock (_lock)
        {
            if (_pool.TryGetValue(value, out var pooled))
                return pooled;

            _pool[value] = value;
            return value;
        }
    }
}

public static class FileRecordStoreSerializer
{
    private const string MetaMagic = "SLRCMETA";
    private const string RecordsMagic = "SLRCREC";
    private const string NamesMagic = "SLRCNAME";
    // v9: $MFT-based one-to-many hard-link index. Bumping this invalidates older single-name caches
    // so existing installs rebuild once and pick up full hard-link paths.
    // v10: force one rebuild to purge records orphaned by incremental USN updates (parent collapsed to a
    // self-parent, shown under the drive root). New caches keep the true parent FRN so it can't recur.
    // v11: force one rebuild to purge children of deleted directories that were never cascade-removed
    // (HardLinkDelta.RemoveLink only marked the directory's own row deleted). Fixed going forward, but
    // existing caches already have the orphaned rows baked in and won't self-heal without a rebuild.
    // v12: records gained Size and Creation/LastWrite/LastAccess timestamps (the latter as a 4-byte
    // whole-second Unix time via FileTimeHelper, not the native 8-byte FILETIME -- a search tool has no
    // use for sub-second precision, and it roughly halves the timestamps' footprint across millions of
    // rows). Existing caches don't carry this data at all, so force one rebuild to populate it instead of
    // leaving old rows permanently zeroed.
    public const int Version = 12;

    public static string GetBasePath(string cacheDir, string sourceKey) => Path.Combine(cacheDir, sourceKey.ToLowerInvariant());

    public static bool Exists(string cacheDir, string sourceKey)
    {
        var basePath = GetBasePath(cacheDir, sourceKey);
        return ExistsBasePath(basePath);
    }

    public static bool ExistsBasePath(string basePath) => File.Exists(basePath + ".meta") &&
                                                          File.Exists(basePath + ".records") &&
                                                          File.Exists(basePath + ".names");

    public static List<string> ListSourceKeys(string cacheDir)
    {
        if (!Directory.Exists(cacheDir))
            return new List<string>();

        return Directory.EnumerateFiles(cacheDir, "*.meta")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.ToUpperInvariant())
            .Where(key => Exists(cacheDir, key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Delete(string cacheDir, string sourceKey) => DeleteBasePath(GetBasePath(cacheDir, sourceKey));

    public static void DeleteBasePath(string basePath)
    {
        TryDelete(basePath + ".meta");
        TryDelete(basePath + ".records");
        TryDelete(basePath + ".names");
    }

    public static void Save(string cacheDir, FileRecordStore store) => Save(cacheDir, store, store.SourceKey);

    public static void Save(string cacheDir, FileRecordStore store, string storageKey)
    {
        store.Records.Sort((a, b) => a.Id.CompareTo(b.Id));
        Directory.CreateDirectory(cacheDir);
        var basePath = GetBasePath(cacheDir, storageKey);
        var metaTemp = basePath + ".meta.tmp";
        var recordsTemp = basePath + ".records.tmp";
        var namesTemp = basePath + ".names.tmp";

        using (var names = new FileStream(namesTemp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var records = new FileStream(recordsTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
        using (var writer = new BinaryWriter(names, Encoding.UTF8))
        using (var recordWriter = new BinaryWriter(records, Encoding.UTF8))
        {
            writer.Write(NamesMagic);
            writer.Write(Version);
            recordWriter.Write(RecordsMagic);
            recordWriter.Write(Version);
            recordWriter.Write(store.Records.Count);

            for (var i = 0; i < store.Records.Count; i++)
            {
                var record = store.Records[i];
                writer.Write(record.Name);
                recordWriter.Write((ulong)record.Id);
                recordWriter.Write((ulong)(record.Id >> 64));
                recordWriter.Write((ulong)record.ParentId);
                recordWriter.Write((ulong)(record.ParentId >> 64));
                recordWriter.Write((ushort)record.Flags);
                recordWriter.Write(record.Size);
                recordWriter.Write(record.CreationTimeUnixSeconds);
                recordWriter.Write(record.LastWriteTimeUnixSeconds);
                recordWriter.Write(record.LastAccessTimeUnixSeconds);
            }
        }

        using (var meta = new FileStream(metaTemp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(meta, Encoding.UTF8))
        {
            writer.Write(MetaMagic);
            writer.Write(Version);
            writer.Write(store.SourceKey);
            writer.Write((byte)store.SourceKind);
            writer.Write((byte)store.IdKind);
            writer.Write(store.FileSystemType);
            writer.Write(store.VolumeSerialNumber);
            writer.Write((ulong)store.RootId);
            writer.Write((ulong)(store.RootId >> 64));
            writer.Write(store.JournalId);
            writer.Write(store.NextUsn);
            writer.Write(store.Records.Count);
            writer.Write(store.Records.Count(r => !r.IsDeleted));
            writer.Write(store.LastUpdated.ToUniversalTime().Ticks);
        }

        Replace(metaTemp, basePath + ".meta");
        Replace(recordsTemp, basePath + ".records");
        Replace(namesTemp, basePath + ".names");
    }

    public static FileRecordStore? Load(string cacheDir, string sourceKey)
    {
        var basePath = GetBasePath(cacheDir, sourceKey);
        try
        {
            if (!Exists(cacheDir, sourceKey))
                return null;

            var store = new FileRecordStore();
            using (var meta = File.OpenRead(basePath + ".meta"))
            using (var reader = new BinaryReader(meta, Encoding.UTF8))
            {
                if (reader.ReadString() != MetaMagic || reader.ReadInt32() != Version)
                    return null;

                store.SourceKey = reader.ReadString();
                store.SourceKind = (FileRecordSourceKind)reader.ReadByte();
                store.IdKind = (FileRecordIdKind)reader.ReadByte();
                store.FileSystemType = reader.ReadString();
                store.VolumeSerialNumber = reader.ReadUInt32();
                var rootLow = reader.ReadUInt64();
                var rootHigh = reader.ReadUInt64();
                store.RootId = new UInt128(rootHigh, rootLow);
                store.JournalId = reader.ReadUInt64();
                store.NextUsn = reader.ReadInt64();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                var ticks = reader.ReadInt64();
                store.LastUpdated = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            }

            var names = new List<string>();
            var namePool = new FileRecordNamePool();
            using (var nameStream = File.OpenRead(basePath + ".names"))
            using (var reader = new BinaryReader(nameStream, Encoding.UTF8))
            {
                if (reader.ReadString() != NamesMagic || reader.ReadInt32() != Version)
                    return null;

                while (nameStream.Position < nameStream.Length)
                {
                    names.Add(namePool.Get(reader.ReadString()));
                }
            }

            using (var records = File.OpenRead(basePath + ".records"))
            using (var reader = new BinaryReader(records, Encoding.UTF8))
            {
                if (reader.ReadString() != RecordsMagic || reader.ReadInt32() != Version)
                    return null;

                var count = reader.ReadInt32();
                store.Records.Capacity = count;
                for (var i = 0; i < count; i++)
                {
                    var idLow = reader.ReadUInt64();
                    var idHigh = reader.ReadUInt64();
                    var parentIdLow = reader.ReadUInt64();
                    var parentIdHigh = reader.ReadUInt64();
                    var id = new UInt128(idHigh, idLow);
                    var parentId = new UInt128(parentIdHigh, parentIdLow);
                    var flags = (FileRecordFlags)reader.ReadUInt16();
                    var size = reader.ReadInt64();
                    var creationTimeUtc = reader.ReadUInt32();
                    var lastWriteTimeUtc = reader.ReadUInt32();
                    var lastAccessTimeUtc = reader.ReadUInt32();
                    store.Records.Add(new FileRecord(
                        id,
                        parentId,
                        i < names.Count ? names[i] : string.Empty,
                        flags,
                        size,
                        creationTimeUtc,
                        lastWriteTimeUtc,
                        lastAccessTimeUtc));
                }
            }

            return store;
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileRecordStoreSerializer] Failed to load {basePath}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    public static FileRecordStoreSummary? LoadSummary(string cacheDir, string sourceKey)
    {
        var basePath = GetBasePath(cacheDir, sourceKey);
        try
        {
            if (!Exists(cacheDir, sourceKey))
                return null;

            using var meta = File.OpenRead(basePath + ".meta");
            using var reader = new BinaryReader(meta, Encoding.UTF8);
            if (reader.ReadString() != MetaMagic || reader.ReadInt32() != Version)
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
        catch (Exception ex)
        {
            Logger.Log($"[FileRecordStoreSerializer] Failed to load summary {basePath}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    private static void Replace(string tempPath, string finalPath)
        => FileRecordStoreReplaceHelper.ReplaceWithRetry(tempPath, finalPath, TryDelete);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
