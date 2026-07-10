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
    // See FileRecordStore.IsComplete -- false for a checkpoint or an interrupted scan, true only once the
    // build that produced this index finished in full.
    public bool IsComplete { get; set; }
    // See FileRecordStore.ExclusionRulesFingerprint.
    public string ExclusionRulesFingerprint { get; set; } = string.Empty;
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
        index.IsComplete = store.IsComplete;
        index.ExclusionRulesFingerprint = store.ExclusionRulesFingerprint;
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
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null,
        FileRecordStore? previousStore = null)
    {
        var index = new NetworkIndex(drive);
        const ulong rootId = 1;
        // Setting this on the store itself (not just on `index` after the walk finishes) means every
        // mid-walk checkpoint -- which serializes this same store, see TreeBuilder.CloneStore -- already
        // carries the right fingerprint too, not just the final save.
        var fingerprint = IndexerHelper.ComputeExclusionFingerprint(options.ExcludedPaths, options.IgnoredPathGlobs, options.IgnoredPathRegexes);
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = rootId,
            ExclusionRulesFingerprint = fingerprint
        };
        // Stat the real root mtime, the same as TryCreateRecord does for every other directory -- without
        // it this record would default to LastWriteTimeUnixSeconds=0, which TreeDiffBaseline could never
        // match against a live stat, permanently forcing the share's own top-level entries to be re-listed
        // on every resume no matter how unchanged they actually are.
        uint rootLastWriteTime = 0;
        try { rootLastWriteTime = FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(physicalRoot)); } catch { }

        store.Records.Add(new FileRecord(
            rootId,
            rootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot,
            lastWriteTimeUnixSeconds: rootLastWriteTime));

        var diffBaseline = TreeDiffBaseline.From(previousStore);
        // No previous store at all means this is a first-ever scan -- nothing to recheck, the normal fresh
        // walk already covers everything. Otherwise, a fingerprint mismatch is the only thing that can make
        // a reused (mtime-unchanged) directory's recorded children incomplete under the *current* rules --
        // see TryReuseUnchangedDirectory's add/remove diff.
        var recheckExclusions = previousStore != null && previousStore.ExclusionRulesFingerprint != fingerprint;
        var builder = new TreeBuilder(store, root, physicalRoot, options, token, onProgress, onCheckpoint, diffBaseline, recheckExclusions);
        var stats = builder.Run();

        index.RootId = rootId;
        index.Skipped = stats.Skipped;
        index.Errors = stats.Errors;
        index.EnumerateErrors = stats.EnumerateErrors;
        index.AttributeErrors = stats.AttributeErrors;
        index.ReparseSkipped = stats.ReparseSkipped;
        index.SlowDirectories = stats.SlowDirectories;
        index.LastUpdated = DateTime.Now;
        index.ExclusionRulesFingerprint = fingerprint;
        lock (index._gate)
            index._runtime.Load(store);
        onProgress(index.Count);
        return index;
    }

    public FileRecordStore ToStore()
    {
        lock (_gate)
        {
            var unc = (Drive.StartsWith(@"\\") || Drive.StartsWith(@"//")) ? Drive : NetworkDriveResolver.GetUncPath(Drive);
            var store = _runtime.ToStore(
                FileRecordSourceKind.NetworkMappedDrive,
                FileRecordIdKind.SourceLocalId64,
                fileSystemType: unc,
                volumeSerialNumber: 0,
                RootId,
                journalId: 0,
                nextUsn: 0);
            store.LastUpdated = LastUpdated;
            store.IsComplete = IsComplete;
            store.ExclusionRulesFingerprint = ExclusionRulesFingerprint;
            return store;
        }
    }


    public void SearchStreaming(ParsedSearchQuery parsed, string rawQuery, string? directoryFilterLower, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        lock (_gate)
            _searcher.SearchStreaming(_runtime, rawQuery, limit, onResult, token, directoryFilterLower);
    }

    // GetFullPath's per-row memo already self-caps at a high threshold (see PathQueryExtensions), but a
    // search window closing/hiding is also a natural point to give the memory back proactively -- mirrors
    // ShellIconHelper.ClearCache()'s existing trigger points.
    public void ClearPathCache()
    {
        lock (_gate)
            _runtime.ClearPathCache();
    }

    // Backs NetworkIndexerRecentFilesExtensions.GetRecentFiles -- same in-memory subtree walk the local
    // NTFS/ReFS path uses (RecentFilesWalker), just pointed at this share's own RuntimeIndex.
    public void CollectRecentFiles(string dirLower, uint cutoffUtc, List<SearchResult> candidates)
    {
        lock (_gate)
            RecentFilesWalker.CollectFromDirectory(_runtime, dirLower, Drive, cutoffUtc, candidates);
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
