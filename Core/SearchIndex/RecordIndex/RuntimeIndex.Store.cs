namespace SwiftList.Core.SearchIndex.RecordIndex;

// Load/ToStore -- the on-disk (de)serialization half of RuntimeIndex, split out to keep RuntimeIndex.cs
// under the line-count limit.
public sealed partial class RuntimeIndex
{
    // A bare drive letter ("Z") needs ":\" appended to form a root. A UNC path just needs a trailing
    // separator. Anything else (a folder-index target, e.g. "Z:\AV") is already a full path -- appending
    // ":\" there would produce "Z:\AV:\", a colon in the middle of the path that can never resolve.
    private static string ComputeSourceRoot(string sourceKey) =>
        sourceKey.StartsWith(@"\\") || sourceKey.StartsWith(@"//") ? (sourceKey.EndsWith(@"\") ? sourceKey : sourceKey + @"\")
        : sourceKey.Length == 1 ? sourceKey + @":\"
        : sourceKey.EndsWith(@"\") ? sourceKey : sourceKey + @"\";

    public void Load(FileRecordStore store)
    {
        Clear();
        SourceKey = store.SourceKey;
        _sourceRoot = ComputeSourceRoot(store.SourceKey);
        _sourceRootLower = _sourceRoot.ToLowerInvariant();

        // Sort store.Records by Id
        store.Records.Sort((a, b) => a.Id.CompareTo(b.Id));

        this.EnsureCapacity(store.Records.Count);

        var parentIds = new List<UInt128>(store.Records.Count);
        var tempAliasIndices = new List<int>();
        var tempAliasValues = new List<string[]>();
        var tempAliasProviderIds = new List<byte[]>();

        foreach (var record in store.Records)
        {
            if (record.IsDeleted)
                continue;

            var index = Count;
            this.AddColumns(
                record.Id,
                record.Name,
                record.Flags,
                record.Size,
                record.CreationTimeUnixSeconds,
                record.LastWriteTimeUnixSeconds,
                record.LastAccessTimeUnixSeconds);
            parentIds.Add(record.ParentId);

            var aliases = this.GenerateAliases(record.Name, out var providerIds);
            if (aliases != null && aliases.Length > 0)
            {
                tempAliasIndices.Add(index);
                tempAliasValues.Add(aliases);
                tempAliasProviderIds.Add(providerIds);
                _charMasks[index] = ulong.MaxValue;
            }

            if (record.IsDirectory)
                TotalDirs++;
            else
                TotalFiles++;
        }

        _loadedCount = Count;
        _aliasIndices = tempAliasIndices.ToArray();
        _aliasValues = tempAliasValues.ToArray();
        _aliasProviderIds = tempAliasProviderIds.ToArray();

        _hasAlias = new System.Collections.BitArray(_loadedCount);
        foreach (var index in _aliasIndices)
        {
            _hasAlias.Set(index, true);
        }

        var resolvedParentIndexes = new int[Count];
        var childCounts = new int[Count];

        for (var index = 0; index < Count; index++)
        {
            var parentId = parentIds[index];
            var parentIndex = -1;
            if (parentId != _ids[index])
            {
                var foundParent = _ids.BinarySearch(parentId);
                if (foundParent >= 0)
                    parentIndex = foundParent;
                else
                    _orphanParentFrns[index] = parentId; // parent not (yet) indexed — keep its true FRN
            }
            resolvedParentIndexes[index] = parentIndex;

            if (parentIndex >= 0)
            {
                childCounts[parentIndex]++;
            }
        }

        _parentIndexes.Capacity = Count;
        for (var index = 0; index < Count; index++)
        {
            _parentIndexes.Add(resolvedParentIndexes[index]);
        }

        for (var index = 0; index < Count; index++)
        {
            var parentIndex = resolvedParentIndexes[index];
            if (parentIndex >= 0)
            {
                if (!_parentToChildren.TryGetValue(parentIndex, out var list))
                {
                    list = new List<int>(childCounts[parentIndex]);
                    _parentToChildren[parentIndex] = list;
                }
                list.Add(index);
            }
        }

        this.BuildNameCharBuckets();
        this.TrimStorage();
        _names.ReleaseLookup();
    }

    public FileRecordStore ToStore(
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
            SourceKey = SourceKey,
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

        store.Records.Capacity = Count;
        for (var i = 0; i < Count; i++)
            store.Records.Add(this.GetRecord(i));
        return store;
    }
}
