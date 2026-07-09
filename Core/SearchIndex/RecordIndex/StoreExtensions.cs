using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core.SearchIndex.RecordIndex;

// Load/ToStore -- the on-disk (de)serialization half of RuntimeIndex -- as extension methods (matching
// BucketExtensions/QueryExtensions/UpdateExtensions) instead of a partial class, to keep RuntimeIndex.cs
// under the line-count limit.
public static class StoreExtensions
{
    public static void Load(this RuntimeIndex index, FileRecordStore store)
    {
        index.Clear();
        index.SourceKey = store.SourceKey;
        index.SourceRoot = PathHelpers.BuildSourceRoot(store.SourceKey);
        index.SourceRootLower = index.SourceRoot.ToLowerInvariant();

        // Sort store.Records by Id
        store.Records.Sort((a, b) => a.Id.CompareTo(b.Id));

        index.EnsureCapacity(store.Records.Count);

        var parentIds = new List<UInt128>(store.Records.Count);
        var tempAliasIndices = new List<int>();
        var tempAliasValues = new List<string[]>();
        var tempAliasProviderIds = new List<byte[]>();

        foreach (var record in store.Records)
        {
            if (record.IsDeleted)
                continue;

            var recordIndex = index.Count;
            index.AddColumns(
                record.Id,
                record.Name,
                record.Flags,
                record.Size,
                record.CreationTimeUnixSeconds,
                record.LastWriteTimeUnixSeconds,
                record.LastAccessTimeUnixSeconds);
            parentIds.Add(record.ParentId);

            var aliases = index.GenerateAliases(record.Name, out var providerIds);
            if (aliases != null && aliases.Length > 0)
            {
                tempAliasIndices.Add(recordIndex);
                tempAliasValues.Add(aliases);
                tempAliasProviderIds.Add(providerIds);
                index.CharMasks[recordIndex] = ulong.MaxValue;
            }

            if (record.IsDirectory)
                index.TotalDirs++;
            else
                index.TotalFiles++;
        }

        index.LoadedCount = index.Count;
        index.AliasIndices = tempAliasIndices.ToArray();
        index.AliasValues = tempAliasValues.ToArray();
        index.AliasProviderIds = tempAliasProviderIds.ToArray();

        index.HasAlias = new System.Collections.BitArray(index.LoadedCount);
        foreach (var aliasIndex in index.AliasIndices)
        {
            index.HasAlias.Set(aliasIndex, true);
        }

        var resolvedParentIndexes = new int[index.Count];
        var childCounts = new int[index.Count];

        for (var i = 0; i < index.Count; i++)
        {
            var parentId = parentIds[i];
            var parentIndex = -1;
            if (parentId != index.Ids[i])
            {
                var foundParent = index.Ids.BinarySearch(parentId);
                if (foundParent >= 0)
                    parentIndex = foundParent;
                else
                    index.OrphanParentFrns[i] = parentId; // parent not (yet) indexed — keep its true FRN
            }
            resolvedParentIndexes[i] = parentIndex;

            if (parentIndex >= 0)
            {
                childCounts[parentIndex]++;
            }
        }

        index.ParentIndexes.Capacity = index.Count;
        for (var i = 0; i < index.Count; i++)
        {
            index.ParentIndexes.Add(resolvedParentIndexes[i]);
        }

        for (var i = 0; i < index.Count; i++)
        {
            var parentIndex = resolvedParentIndexes[i];
            if (parentIndex >= 0)
            {
                if (!index.ParentToChildren.TryGetValue(parentIndex, out var list))
                {
                    list = new List<int>(childCounts[parentIndex]);
                    index.ParentToChildren[parentIndex] = list;
                }
                list.Add(i);
            }
        }

        index.BuildNameCharBuckets();
        index.TrimStorage();
        index.Names.ReleaseLookup();
    }

    public static FileRecordStore ToStore(
        this RuntimeIndex index,
        FileRecordSourceKind sourceKind,
        FileRecordIdKind idKind,
        string fileSystemType,
        uint volumeSerialNumber,
        UInt128 rootId,
        ulong journalId,
        long nextUsn)
    {
        var store = new FileRecordStore
        {
            SourceKey = index.SourceKey,
            SourceKind = sourceKind,
            IdKind = idKind,
            FileSystemType = fileSystemType,
            VolumeSerialNumber = volumeSerialNumber,
            RootId = rootId,
            JournalId = journalId,
            NextUsn = nextUsn,
            // Local USN/MFT snapshots have no partial-checkpoint concept -- a runtime index only ever gets
            // converted back to a store once fully (re)built or patched, so this is always true for them.
            // NetworkIndex.ToStore() overwrites this with its own IsComplete right after calling here.
            IsComplete = true
        };

        store.Records.Capacity = index.Count;
        for (var i = 0; i < index.Count; i++)
            store.Records.Add(index.GetRecord(i));
        return store;
    }
}
