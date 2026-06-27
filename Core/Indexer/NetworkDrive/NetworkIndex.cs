using SwiftList.Core.SearchIndex.RecordIndex;
using SwiftList.Core.SearchIndex.RecordSearch;
using SwiftList.Core.Indexer.Shared;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal sealed class NetworkIndex
{
    private readonly object _gate = new();
    private readonly RuntimeIndex _runtime = new();
    private readonly Searcher _searcher = new();

    public NetworkIndex(string drive)
    {
        Drive = drive;
        // ponytail: restrict background search thread usage inside UI process to prevent stutter
        _searcher.MaxDegreeOfParallelism = 2;
    }

    public string Drive { get; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public UInt128 RootId { get; private set; }
    public int Skipped { get; private set; }
    public int Errors { get; private set; }
    public int EnumerateErrors { get; private set; }
    public int AttributeErrors { get; private set; }
    public int ReparseSkipped { get; private set; }
    public int SlowDirectories { get; private set; }
    public int Count
    {
        get
        {
            lock (_gate)
                return Math.Max(0, _runtime.TotalFiles + _runtime.TotalDirs - 1);
        }
    }

    public static NetworkIndex FromStore(FileRecordStore store)
    {
        var index = new NetworkIndex(store.SourceKey);
        index.RootId = store.RootId;
        index.LastUpdated = store.LastUpdated;
        lock (index._gate)
            index._runtime.Load(store);
        return index;
    }

    public static NetworkIndex FromStore(FileRecordStore store, NetworkDriveWalkStats stats)
    {
        var index = FromStore(store);
        index.Skipped = stats.Skipped;
        index.Errors = stats.Errors;
        index.EnumerateErrors = stats.EnumerateErrors;
        index.AttributeErrors = stats.AttributeErrors;
        index.ReparseSkipped = stats.ReparseSkipped;
        index.SlowDirectories = stats.SlowDirectories;
        index.LastUpdated = DateTime.Now;
        return index;
    }

    public static NetworkIndex Build(
        string drive,
        string root,
        string physicalRoot,
        WalkOptions options,
        CancellationToken token,
        Action<int> onProgress,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null)
    {
        var index = new NetworkIndex(drive);
        const ulong rootId = 1;
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = rootId
        };
        store.Records.Add(new FileRecord(
            rootId,
            rootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        var builder = new TreeBuilder(store, root, physicalRoot, options, token, onProgress, onCheckpoint);
        var stats = builder.Run();

        index.RootId = rootId;
        index.Skipped = stats.Skipped;
        index.Errors = stats.Errors;
        index.EnumerateErrors = stats.EnumerateErrors;
        index.AttributeErrors = stats.AttributeErrors;
        index.ReparseSkipped = stats.ReparseSkipped;
        index.SlowDirectories = stats.SlowDirectories;
        index.LastUpdated = DateTime.Now;
        lock (index._gate)
            index._runtime.Load(store);
        onProgress(index.Count);
        return index;
    }

    public FileRecordStore ToStore()
    {
        lock (_gate)
        {
            var store = _runtime.ToStore(
                FileRecordSourceKind.NetworkMappedDrive,
                FileRecordIdKind.SourceLocalId64,
                fileSystemType: string.Empty,
                volumeSerialNumber: 0,
                RootId,
                journalId: 0,
                nextUsn: 0);
            store.LastUpdated = LastUpdated;
            return store;
        }
    }


    public void SearchStreaming(ParsedSearchQuery parsed, string rawQuery, string? directoryFilterLower, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        lock (_gate)
            _searcher.SearchStreaming(_runtime, rawQuery, limit, onResult, token, directoryFilterLower);
    }

    public bool ApplyCreatedOrChanged(string root, string path, ExclusionRuleSet? exclusionRules = null)
    {
        lock (_gate)
        {
            var changed = PathDeltaApplier.ApplyCreatedOrChanged(_runtime, RootId, root, path, exclusionRules);
            if (changed)
            {
                LastUpdated = DateTime.Now;
                _searcher.ClearCaches();
            }
            return changed;
        }
    }

    public bool ApplyDeleted(string path)
    {
        lock (_gate)
        {
            var removed = PathDeltaApplier.ApplyDeleted(_runtime, path);
            if (removed)
            {
                LastUpdated = DateTime.Now;
                _searcher.ClearCaches();
            }
            return removed;
        }
    }

    public bool ApplyRenamed(string root, string oldPath, string newPath, ExclusionRuleSet? exclusionRules = null)
    {
        lock (_gate)
        {
            var changed = PathDeltaApplier.ApplyRenamed(_runtime, RootId, root, oldPath, newPath, exclusionRules);
            if (changed)
            {
                LastUpdated = DateTime.Now;
                _searcher.ClearCaches();
            }
            return changed;
        }
    }
}
