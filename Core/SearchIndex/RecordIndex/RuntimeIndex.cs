namespace SwiftList.Core.SearchIndex.RecordIndex;

public sealed class RuntimeIndex
{
    private int _loadedCount;
    private readonly Dictionary<UInt128, int> _deltaIdToIndex = new();
    private readonly Dictionary<char, List<int>> _nameCharDelta = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<UInt128, string> _pathMemo = new();
    private readonly List<UInt128> _ids = new();
    private readonly List<int> _parentIndexes = new();
    private readonly List<int> _nameIds = new();
    private readonly List<byte> _flags = new();
    private readonly List<ulong> _charMasks = new();
    private int[] _aliasIndices = Array.Empty<int>();
    private string[][] _aliasValues = Array.Empty<string[]>();
    private readonly Dictionary<int, string[]> _deltaNameAliases = new();
    private System.Collections.BitArray _hasAlias = new(0);
    private readonly NameTable _names = new();
    private Dictionary<char, int[]> _nameCharBuckets = new();
    private string _sourceRoot = string.Empty;
    private string _sourceRootLower = string.Empty;

    public string SourceKey { get; private set; } = string.Empty;
    public int Count => _ids.Count;
    public int TotalFiles { get; internal set; }
    public int TotalDirs { get; internal set; }

    internal readonly record struct ChildRange(int Start, int Count);

    // Internal properties to expose underlying storage to extension methods
    internal List<UInt128> Ids => _ids;
    internal List<int> ParentIndexes => _parentIndexes;
    internal List<int> NameIds => _nameIds;
    internal List<byte> Flags => _flags;
    internal List<ulong> CharMasks => _charMasks;
    internal int LoadedCount
    {
        get => _loadedCount;
        set => _loadedCount = value;
    }
    internal Dictionary<UInt128, int> DeltaIdToIndex => _deltaIdToIndex;
    internal Dictionary<char, List<int>> NameCharDelta => _nameCharDelta;
    internal System.Collections.Concurrent.ConcurrentDictionary<UInt128, string> PathMemo => _pathMemo;
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
    internal Dictionary<char, int[]> NameCharBuckets => _nameCharBuckets;

    internal void SetNameCharBuckets(Dictionary<char, int[]> buckets) => _nameCharBuckets = buckets;

    internal string SourceRoot => _sourceRoot;
    internal string SourceRootLower => _sourceRootLower;

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
        _deltaIdToIndex.Clear();
        _aliasIndices = Array.Empty<int>();
        _aliasValues = Array.Empty<string[]>();
        _deltaNameAliases.Clear();
        _hasAlias = new System.Collections.BitArray(0);
        _nameCharDelta.Clear();
        _nameCharBuckets.Clear();
        _pathMemo.Clear();
        _loadedCount = 0;
        TotalFiles = 0;
        TotalDirs = 0;
    }

    public void Load(FileRecordStore store)
    {
        Clear();
        SourceKey = store.SourceKey;
        _sourceRoot = store.SourceKey + @":\";
        _sourceRootLower = _sourceRoot.ToLowerInvariant();

        // Sort store.Records by Id
        store.Records.Sort((a, b) => a.Id.CompareTo(b.Id));

        this.EnsureCapacity(store.Records.Count);

        var parentIds = new List<UInt128>(store.Records.Count);
        var tempAliasIndices = new List<int>();
        var tempAliasValues = new List<string[]>();

        foreach (var record in store.Records)
        {
            if (record.IsDeleted)
                continue;

            var index = Count;
            this.AddColumns(
                record.Id,
                record.Name,
                record.Flags);
            parentIds.Add(record.ParentId);

            var aliases = this.GenerateAliases(record.Name);
            if (aliases != null && aliases.Length > 0)
            {
                tempAliasIndices.Add(index);
                tempAliasValues.Add(aliases);
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

        _hasAlias = new System.Collections.BitArray(_loadedCount);
        foreach (var index in _aliasIndices)
        {
            _hasAlias.Set(index, true);
        }

        for (var index = 0; index < Count; index++)
        {
            var parentId = parentIds[index];
            var parentIndex = -1;
            if (parentId != _ids[index])
            {
                var foundParent = _ids.BinarySearch(parentId);
                if (foundParent >= 0)
                {
                    parentIndex = foundParent;
                }
            }
            _parentIndexes.Add(parentIndex);
        }

        this.BuildNameCharBuckets();
        this.TrimStorage();
        _names.ReleaseLookup();
    }

    public FileRecordStore ToStore(
        FileRecordSourceKind sourceKind,
        FileRecordIdKind idKind,
        UInt128 rootId,
        ulong journalId,
        long nextUsn)
    {
        var store = new FileRecordStore
        {
            SourceKey = SourceKey,
            SourceKind = sourceKind,
            IdKind = idKind,
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
