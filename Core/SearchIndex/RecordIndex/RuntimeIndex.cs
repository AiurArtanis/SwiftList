namespace SwiftList.Core.SearchIndex.RecordIndex;

public sealed class RuntimeIndex
{
    private int _loadedCount;
    private readonly Dictionary<UInt128, int> _deltaIdToIndex = new();
    private readonly Dictionary<char, List<int>> _nameCharDelta = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _pathMemo = new();
    private readonly Dictionary<UInt128, List<int>> _hardLinkDeltaRows = new();
    // Rows whose parent FRN wasn't in the index when they were linked (parentIndex == -1). Keyed by row
    // index — safe because the index never physically compacts/reorders rows (deletes are a soft flag).
    // Keeps the true parent FRN so GetParentId round-trips it losslessly (instead of collapsing to a
    // self-parent) and GetFullPath can resolve the real path once the parent directory is indexed.
    // Concurrent because GetFullPath reads it on the lock-free search path while USN updates write it
    // (same reason PathMemo is concurrent).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, UInt128> _orphanParentFrns = new();
    private readonly List<UInt128> _ids = new();
    private readonly List<int> _parentIndexes = new();
    private readonly List<int> _nameIds = new();
    private readonly List<byte> _flags = new();
    private readonly List<ulong> _charMasks = new();
    private readonly List<long> _sizes = new();
    private readonly List<uint> _creationTimes = new();
    private readonly List<uint> _lastWriteTimes = new();
    private readonly List<uint> _lastAccessTimes = new();
    private int[] _aliasIndices = Array.Empty<int>();
    private string[][] _aliasValues = Array.Empty<string[]>();
    private byte[][] _aliasProviderIds = Array.Empty<byte[]>();
    private readonly Dictionary<int, string[]> _deltaNameAliases = new();
    private readonly Dictionary<int, byte[]> _deltaAliasProviderIds = new();
    private System.Collections.BitArray _hasAlias = new(0);
    private readonly NameTable _names = new();
    private Dictionary<char, int[]> _nameCharBuckets = new();
    private readonly Dictionary<int, List<int>> _parentToChildren = new();
    private string _sourceRoot = string.Empty;
    private string _sourceRootLower = string.Empty;

    public string SourceKey { get; internal set; } = string.Empty;
    public int Count => _ids.Count;
    public int TotalFiles { get; internal set; }
    public int TotalDirs { get; internal set; }

    internal Dictionary<int, List<int>> ParentToChildren => _parentToChildren;
    public bool TryGetChildren(int parentIndex, out List<int>? children) => _parentToChildren.TryGetValue(parentIndex, out children);

    internal readonly record struct ChildRange(int Start, int Count);

    // Internal properties to expose underlying storage to extension methods
    internal List<UInt128> Ids => _ids;
    internal List<int> ParentIndexes => _parentIndexes;
    internal List<int> NameIds => _nameIds;
    internal List<byte> Flags => _flags;
    internal List<ulong> CharMasks => _charMasks;
    internal List<long> Sizes => _sizes;
    internal List<uint> CreationTimes => _creationTimes;
    internal List<uint> LastWriteTimes => _lastWriteTimes;
    internal List<uint> LastAccessTimes => _lastAccessTimes;
    internal int LoadedCount
    {
        get => _loadedCount;
        set => _loadedCount = value;
    }
    internal Dictionary<UInt128, int> DeltaIdToIndex => _deltaIdToIndex;
    internal Dictionary<char, List<int>> NameCharDelta => _nameCharDelta;
    internal System.Collections.Concurrent.ConcurrentDictionary<int, string> PathMemo => _pathMemo;
    internal System.Collections.Concurrent.ConcurrentDictionary<int, UInt128> OrphanParentFrns => _orphanParentFrns;

    // Records/clears a row's stashed true parent FRN as its parent link is (re)computed incrementally.
    internal void TrackOrphanParent(int row, int parentIndex, UInt128 parentId)
    {
        if (parentIndex < 0 && parentId != _ids[row])
            _orphanParentFrns[row] = parentId;
        else
            _orphanParentFrns.TryRemove(row, out _);
    }

    // Extra rows appended for a hard-linked FRN by incremental one-to-many maintenance (delta region).
    internal Dictionary<UInt128, List<int>> HardLinkDeltaRows => _hardLinkDeltaRows;
    internal NameTable Names => _names;
    internal Dictionary<int, string[]> DeltaNameAliases => _deltaNameAliases;
    internal System.Collections.BitArray HasAlias
    {
        get => _hasAlias;
        set => _hasAlias = value;
    }
    internal int[] AliasIndices
    {
        get => _aliasIndices;
        set => _aliasIndices = value;
    }
    internal string[][] AliasValues
    {
        get => _aliasValues;
        set => _aliasValues = value;
    }
    internal byte[][] AliasProviderIds
    {
        get => _aliasProviderIds;
        set => _aliasProviderIds = value;
    }
    internal Dictionary<int, byte[]> DeltaAliasProviderIds => _deltaAliasProviderIds;
    internal Dictionary<char, int[]> NameCharBuckets => _nameCharBuckets;

    internal void SetNameCharBuckets(Dictionary<char, int[]> buckets) => _nameCharBuckets = buckets;

    internal string SourceRoot
    {
        get => _sourceRoot;
        set => _sourceRoot = value;
    }
    internal string SourceRootLower
    {
        get => _sourceRootLower;
        set => _sourceRootLower = value;
    }

    public void Clear()
    {
        SourceKey = string.Empty;
        _sourceRoot = string.Empty;
        _sourceRootLower = string.Empty;
        _ids.Clear();
        _parentIndexes.Clear();
        _nameIds.Clear();
        _names.Clear();
        _flags.Clear();
        _charMasks.Clear();
        _sizes.Clear();
        _creationTimes.Clear();
        _lastWriteTimes.Clear();
        _lastAccessTimes.Clear();
        _deltaIdToIndex.Clear();
        _aliasIndices = Array.Empty<int>();
        _aliasValues = Array.Empty<string[]>();
        _aliasProviderIds = Array.Empty<byte[]>();
        _deltaNameAliases.Clear();
        _deltaAliasProviderIds.Clear();
        _hasAlias = new System.Collections.BitArray(0);
        _parentToChildren.Clear();
        _nameCharDelta.Clear();
        _nameCharBuckets.Clear();
        _pathMemo.Clear();
        _hardLinkDeltaRows.Clear();
        _orphanParentFrns.Clear();
        _loadedCount = 0;
        TotalFiles = 0;
        TotalDirs = 0;
    }

    public void Load(FileRecordStore store)
    {
        Clear();
        SourceKey = store.SourceKey;
        _sourceRoot = (store.SourceKey.StartsWith(@"\\") || store.SourceKey.StartsWith(@"//"))
            ? (store.SourceKey.EndsWith(@"\") ? store.SourceKey : store.SourceKey + @"\")
            : store.SourceKey + @":\";
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
            NextUsn = nextUsn
        };

        store.Records.Capacity = Count;
        for (var i = 0; i < Count; i++)
            store.Records.Add(this.GetRecord(i));
        return store;
    }

}
