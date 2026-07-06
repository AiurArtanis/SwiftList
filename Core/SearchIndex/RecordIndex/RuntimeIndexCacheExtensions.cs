using System.Text;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.SearchIndex.RecordIndex;

internal static class RuntimeIndexCacheExtensions
{
    internal static UsnIndexer.DriveRuntimeMetadata? LoadFromCacheDirect(this RuntimeIndex index, string basePath)
    {
        index.Clear();
        if (!FileRecordStoreSerializer.ExistsBasePath(basePath))
        {
            Logger.Log($"[RuntimeIndex] Cache base path does not exist: {basePath}", LogLevel.Error);
            return null;
        }

        UsnIndexer.DriveRuntimeMetadata metadata;
        int count;

        try
        {
            using var meta = File.OpenRead(basePath + ".meta");
            using var reader = new BinaryReader(meta, Encoding.UTF8);
            var magic = reader.ReadString();
            var ver = reader.ReadInt32();
            if (magic != "SLRCMETA" || ver != FileRecordStoreSerializer.Version)
            {
                Logger.Log($"[RuntimeIndex] Meta magic/version mismatch. Magic: {magic}, Ver: {ver}", LogLevel.Error);
                return null;
            }

            index.SourceKey = reader.ReadString();
            index.SourceRoot = index.SourceKey + @":\";
            index.SourceRootLower = index.SourceRoot.ToLowerInvariant();

            var sourceKind = (FileRecordSourceKind)reader.ReadByte();
            var idKind = (FileRecordIdKind)reader.ReadByte();
            var fileSystemType = reader.ReadString();
            var volumeSerialNumber = reader.ReadUInt32();
            var rootLow = reader.ReadUInt64();
            var rootHigh = reader.ReadUInt64();
            var rootId = new UInt128(rootHigh, rootLow);
            var journalId = reader.ReadUInt64();
            var nextUsn = reader.ReadInt64();
            count = reader.ReadInt32(); // store.Records.Count
            _ = reader.ReadInt32(); // non-deleted count
            var ticks = reader.ReadInt64();

            metadata = new UsnIndexer.DriveRuntimeMetadata
            {
                SourceKind = sourceKind,
                IdKind = idKind,
                FileSystemType = fileSystemType,
                VolumeSerialNumber = volumeSerialNumber,
                RootId = rootId,
                JournalId = journalId,
                NextUsn = nextUsn
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"[RuntimeIndex] Exception reading metadata for {basePath}: {ex.Message}", LogLevel.Error);
            return null;
        }

        index.EnsureCapacity(count);

        var parentIds = new List<UInt128>(count);
        var tempAliasIndices = new List<int>();
        var tempAliasValues = new List<string[]>();
        var tempAliasProviderIds = new List<byte[]>();

        var namePool = new FileRecordNamePool();

        try
        {
            using var nameStream = File.OpenRead(basePath + ".names");
            using var recordStream = File.OpenRead(basePath + ".records");
            using var nameReader = new BinaryReader(nameStream, Encoding.UTF8);
            using var recordReader = new BinaryReader(recordStream, Encoding.UTF8);
            var nameMagic = nameReader.ReadString();
            var nameVer = nameReader.ReadInt32();
            if (nameMagic != "SLRCNAME" || nameVer != FileRecordStoreSerializer.Version)
            {
                Logger.Log($"[RuntimeIndex] Names magic/version mismatch. Magic: {nameMagic}, Ver: {nameVer}", LogLevel.Error);
                return null;
            }

            var recMagic = recordReader.ReadString();
            var recVer = recordReader.ReadInt32();
            if (recMagic != "SLRCREC" || recVer != FileRecordStoreSerializer.Version)
            {
                Logger.Log($"[RuntimeIndex] Records magic/version mismatch. Magic: {recMagic}, Ver: {recVer}", LogLevel.Error);
                return null;
            }

            var recordCount = recordReader.ReadInt32();
            if (recordCount != count)
            {
                Logger.Log($"[RuntimeIndex] Record count mismatch. Meta: {count}, Records: {recordCount}", LogLevel.Error);
                return null;
            }

            for (var i = 0; i < count; i++)
            {
                var name = namePool.Get(nameReader.ReadString());

                var idLow = recordReader.ReadUInt64();
                var idHigh = recordReader.ReadUInt64();
                var parentIdLow = recordReader.ReadUInt64();
                var parentIdHigh = recordReader.ReadUInt64();
                var flags = (FileRecordFlags)recordReader.ReadUInt16();
                var size = recordReader.ReadInt64();
                var creationTimeUtc = recordReader.ReadUInt32();
                var lastWriteTimeUtc = recordReader.ReadUInt32();
                var lastAccessTimeUtc = recordReader.ReadUInt32();

                var id = new UInt128(idHigh, idLow);
                var parentId = new UInt128(parentIdHigh, parentIdLow);

                var indexVal = index.Count;
                index.AddColumns(id, name, flags, size, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc);
                parentIds.Add(parentId);

                var aliases = index.GenerateAliases(name, out var providerIds);
                if (aliases != null && aliases.Length > 0)
                {
                    tempAliasIndices.Add(indexVal);
                    tempAliasValues.Add(aliases);
                    tempAliasProviderIds.Add(providerIds);
                    index.CharMasks[indexVal] = ulong.MaxValue;
                }

                var isDirectory = (flags & FileRecordFlags.Directory) != 0;
                if (isDirectory)
                    index.TotalDirs++;
                else
                    index.TotalFiles++;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[RuntimeIndex] Exception reading records for {basePath}: {ex.Message}", LogLevel.Error);
            return null;
        }

        index.LoadedCount = index.Count;
        index.AliasIndices = tempAliasIndices.ToArray();
        index.AliasValues = tempAliasValues.ToArray();
        index.AliasProviderIds = tempAliasProviderIds.ToArray();

        index.HasAlias = new System.Collections.BitArray(index.LoadedCount);
        foreach (var idx in index.AliasIndices)
        {
            index.HasAlias.Set(idx, true);
        }

        var resolvedParentIndexes = new int[index.Count];
        var childCounts = new int[index.Count];

        for (var idx = 0; idx < index.Count; idx++)
        {
            var parentId = parentIds[idx];
            var parentIndex = -1;
            if (parentId != index.Ids[idx])
            {
                var foundParent = index.Ids.BinarySearch(parentId);
                if (foundParent >= 0)
                    parentIndex = foundParent;
                else
                    index.OrphanParentFrns[idx] = parentId; // parent not (yet) indexed — keep its true FRN
            }
            resolvedParentIndexes[idx] = parentIndex;

            if (parentIndex >= 0)
            {
                childCounts[parentIndex]++;
            }
        }

        index.ParentIndexes.Capacity = index.Count;
        for (var idx = 0; idx < index.Count; idx++)
        {
            index.ParentIndexes.Add(resolvedParentIndexes[idx]);
        }

        for (var idx = 0; idx < index.Count; idx++)
        {
            var parentIndex = resolvedParentIndexes[idx];
            if (parentIndex >= 0)
            {
                if (!index.ParentToChildren.TryGetValue(parentIndex, out var list))
                {
                    list = new List<int>(childCounts[parentIndex]);
                    index.ParentToChildren[parentIndex] = list;
                }
                list.Add(idx);
            }
        }

        index.BuildNameCharBuckets();
        index.TrimStorage();
        index.Names.ReleaseLookup();

        return metadata;
    }
}
