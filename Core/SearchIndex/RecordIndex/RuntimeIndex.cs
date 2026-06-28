using System.Text;

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
    private byte[][] _aliasProviderIds = Array.Empty<byte[]>();
    private readonly Dictionary<int, string[]> _deltaNameAliases = new();
    private readonly Dictionary<int, byte[]> _deltaAliasProviderIds = new();
    private System.Collections.BitArray _hasAlias = new(0);
    private readonly NameTable _names = new();
    private Dictionary<char, int[]> _nameCharBuckets = new();
    private readonly Dictionary<int, List<int>> _parentToChildren = new();
    private string _sourceRoot = string.Empty;
    private string _sourceRootLower = string.Empty;

    public string SourceKey { get; private set; } = string.Empty;
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
    internal byte[][] AliasProviderIds
    {
        get => _aliasProviderIds;
        set => _aliasProviderIds = value;
    }
    internal Dictionary<int, byte[]> DeltaAliasProviderIds => _deltaAliasProviderIds;
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
        _aliasProviderIds = Array.Empty<byte[]>();
        _deltaNameAliases.Clear();
        _deltaAliasProviderIds.Clear();
        _hasAlias = new System.Collections.BitArray(0);
        _parentToChildren.Clear();
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
        var tempAliasProviderIds = new List<byte[]>();

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
                {
                    parentIndex = foundParent;
                }
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

    internal Indexer.Usn.UsnIndexer.DriveRuntimeMetadata? LoadFromCacheDirect(string basePath)
    {
        Clear();
        if (!FileRecordStoreSerializer.ExistsBasePath(basePath))
        {
            Logger.Log($"[RuntimeIndex] Cache base path does not exist: {basePath}", LogLevel.Error);
            return null;
        }

        Indexer.Usn.UsnIndexer.DriveRuntimeMetadata metadata;
        int count;

        try
        {
            using var meta = File.OpenRead(basePath + ".meta");
            using var reader = new BinaryReader(meta, Encoding.UTF8);
            var magic = reader.ReadString();
            var ver = reader.ReadInt32();
            if (magic != "SLRCMETA" || ver != 7)
            {
                Logger.Log($"[RuntimeIndex] Meta magic/version mismatch. Magic: {magic}, Ver: {ver}", LogLevel.Error);
                return null;
            }

            SourceKey = reader.ReadString();
            _sourceRoot = SourceKey + @":\";
            _sourceRootLower = _sourceRoot.ToLowerInvariant();

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

            metadata = new Indexer.Usn.UsnIndexer.DriveRuntimeMetadata
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

        this.EnsureCapacity(count);

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
            if (nameMagic != "SLRCNAME" || nameVer != 7)
            {
                Logger.Log($"[RuntimeIndex] Names magic/version mismatch. Magic: {nameMagic}, Ver: {nameVer}", LogLevel.Error);
                return null;
            }

            var recMagic = recordReader.ReadString();
            var recVer = recordReader.ReadInt32();
            if (recMagic != "SLRCREC" || recVer != 7)
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

                var id = new UInt128(idHigh, idLow);
                var parentId = new UInt128(parentIdHigh, parentIdLow);

                var index = Count;
                this.AddColumns(id, name, flags);
                parentIds.Add(parentId);

                var aliases = this.GenerateAliases(name, out var providerIds);
                if (aliases != null && aliases.Length > 0)
                {
                    tempAliasIndices.Add(index);
                    tempAliasValues.Add(aliases);
                    tempAliasProviderIds.Add(providerIds);
                    _charMasks[index] = ulong.MaxValue;
                }

                var isDirectory = (flags & FileRecordFlags.Directory) != 0;
                if (isDirectory)
                    TotalDirs++;
                else
                    TotalFiles++;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[RuntimeIndex] Exception reading records for {basePath}: {ex.Message}", LogLevel.Error);
            return null;
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
                {
                    parentIndex = foundParent;
                }
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

        return metadata;
    }
}
