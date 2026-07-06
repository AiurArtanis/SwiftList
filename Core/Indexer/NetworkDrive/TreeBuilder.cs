using System.Diagnostics;
using System.Threading.Channels;

namespace SwiftList.Core.Indexer.NetworkDrive;

internal sealed class TreeBuilder
{
    private const int RecordBatchSize = 256;
    private const int ProgressBatchSize = 1024;
    private const int CheckpointBatchSize = 4096;
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(5);
    private readonly FileRecordStore _store;
    private readonly string _root;
    private readonly string _physicalRoot;
    private readonly WalkFilter _filter;
    private readonly CancellationToken _token;
    private readonly Action<int> _onProgress;
    private readonly Action<FileRecordStore, NetworkDriveWalkStats>? _onCheckpoint;
    private readonly Channel<WorkItem> _pending;
    private readonly object _recordsGate = new();
    private readonly FileRecordNamePool _namePool = new();
    private int _pendingDirectories;
    private int _countSinceProgress;
    private int _indexedItems;
    private int _skippedItems;
    private int _errors;
    private int _enumerateErrors;
    private int _attributeErrors;
    private int _reparseSkipped;
    private int _slowDirectories;
    private int _countSinceCheckpoint;
    private long _lastCheckpointTicks = DateTime.UtcNow.Ticks;

    public TreeBuilder(
        FileRecordStore store,
        string root,
        string physicalRoot,
        WalkOptions options,
        CancellationToken token,
        Action<int> onProgress,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null)
    {
        _store = store;
        _root = PathHelpers.NormalizePath(root, true);
        _physicalRoot = PathHelpers.NormalizePath(physicalRoot, true);
        _filter = WalkFilter.Create(_root, options);
        _token = token;
        _onProgress = onProgress;
        _onCheckpoint = onCheckpoint;
        _pending = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public NetworkDriveWalkStats Run()
    {
        EnqueueDirectory(_physicalRoot, _root, parentId: 1, depth: 0, NetworkIgnoreRuleSet.Empty);
        var workers = GetWorkerCount();
        var tasks = new Task[workers];
        for (var i = 0; i < workers; i++)
            tasks[i] = Task.Run(WorkerLoopAsync, _token);

        Task.WaitAll(tasks, _token);
        return new NetworkDriveWalkStats(
            Volatile.Read(ref _skippedItems),
            Volatile.Read(ref _errors),
            Volatile.Read(ref _enumerateErrors),
            Volatile.Read(ref _attributeErrors),
            Volatile.Read(ref _reparseSkipped),
            Volatile.Read(ref _slowDirectories));
    }

    private async Task WorkerLoopAsync()
    {
        var reader = _pending.Reader;
        while (await reader.WaitToReadAsync(_token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var current))
            {
                _token.ThrowIfCancellationRequested();
                WalkDirectory(current);
                if (Interlocked.Decrement(ref _pendingDirectories) == 0)
                    _pending.Writer.TryComplete();
            }
        }
    }

    private void WalkDirectory(WorkItem current)
    {
        var ignoreRules = _filter.LoadIgnoreRules(current.Path, current.LogicalPath, current.IgnoreRules);
        var stopwatch = Stopwatch.StartNew();
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(current.Path);
        }
        catch
        {
            CountError(ref _enumerateErrors);
            return;
        }

        var batch = new List<FileRecord>(RecordBatchSize);
        foreach (var child in children)
        {
            _token.ThrowIfCancellationRequested();

            var createResult = TryCreateRecord(child, current.LogicalPath, current.LocalId, out var record, out var isDirectory, out var logicalFullPath);
            if (createResult != WalkRecordResult.Success)
            {
                CountCreateFailure(createResult);
                continue;
            }

            if (!_filter.ShouldIndex(logicalFullPath, record.Name, isDirectory, record.Attributes, ignoreRules))
            {
                Interlocked.Increment(ref _skippedItems);
                continue;
            }

            batch.Add(record);
            if (batch.Count >= RecordBatchSize)
                FlushRecords(batch);

            var indexedItems = Interlocked.Increment(ref _indexedItems);

            if (isDirectory && _filter.ShouldDescend(logicalFullPath, record.Attributes, current.Depth + 1, ignoreRules))
                EnqueueDirectory(child, logicalFullPath, record.Id, current.Depth + 1, ignoreRules);

            if (Interlocked.Increment(ref _countSinceProgress) >= ProgressBatchSize)
            {
                Interlocked.Exchange(ref _countSinceProgress, 0);
                _onProgress(indexedItems);
            }

            MaybeCheckpoint(indexedItems);
        }

        FlushRecords(batch);
        if (stopwatch.ElapsedMilliseconds >= 2_000)
            Interlocked.Increment(ref _slowDirectories);
    }

    private WalkRecordResult TryCreateRecord(string child, string logicalParentPath, UInt128 parentId, out NetworkWalkRecord record, out bool isDirectory, out string fullPath)
    {
        record = default;
        isDirectory = false;
        fullPath = string.Empty;

        FileInfo info;
        FileAttributes attributes;
        try
        {
            info = new FileInfo(child);
            attributes = info.Attributes;
        }
        catch
        {
            return WalkRecordResult.AttributeError;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return WalkRecordResult.ReparsePoint;

        isDirectory = (attributes & FileAttributes.Directory) != 0;
        var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            return WalkRecordResult.InvalidName;

        var logicalPath = Path.Combine(logicalParentPath, name);
        fullPath = PathHelpers.NormalizePath(logicalPath, isDirectory);
        var id = PathHelpers.HashPath64(fullPath);
        var flags = FileRecordFlagsHelper.FromAttributes(attributes);
        // Length can still throw on a flaky network share even though Attributes just succeeded; that's
        // supplementary metadata, not worth failing the whole record over.
        long size = 0;
        if (!isDirectory)
        {
            try { size = info.Length; } catch { }
        }
        var fileRecord = new FileRecord(
            id,
            parentId,
            _namePool.Get(name),
            flags,
            size,
            info.CreationTimeUtc.ToFileTimeUtc(),
            info.LastWriteTimeUtc.ToFileTimeUtc(),
            info.LastAccessTimeUtc.ToFileTimeUtc());
        record = new NetworkWalkRecord(fileRecord, attributes);
        return WalkRecordResult.Success;
    }

    private void CountCreateFailure(WalkRecordResult result)
    {
        switch (result)
        {
            case WalkRecordResult.AttributeError:
                CountError(ref _attributeErrors);
                break;
            case WalkRecordResult.ReparsePoint:
                Interlocked.Increment(ref _reparseSkipped);
                Interlocked.Increment(ref _skippedItems);
                break;
            default:
                Interlocked.Increment(ref _skippedItems);
                break;
        }
    }

    private void CountError(ref int counter)
    {
        Interlocked.Increment(ref counter);
        Interlocked.Increment(ref _errors);
    }

    private void FlushRecords(List<FileRecord> batch)
    {
        if (batch.Count == 0)
            return;

        lock (_recordsGate)
        {
            _store.Records.AddRange(batch);
        }

        batch.Clear();
    }

    private void MaybeCheckpoint(int indexedItems)
    {
        if (_onCheckpoint == null)
            return;

        var nowTicks = DateTime.UtcNow.Ticks;
        var count = Interlocked.Increment(ref _countSinceCheckpoint);
        var countDue = count >= CheckpointBatchSize;
        var timeDue = new TimeSpan(nowTicks - Interlocked.Read(ref _lastCheckpointTicks)) >= CheckpointInterval;
        if (!countDue && !timeDue)
            return;

        if (Interlocked.Exchange(ref _countSinceCheckpoint, 0) == 0 && !timeDue)
            return;

        Interlocked.Exchange(ref _lastCheckpointTicks, nowTicks);
        _onProgress(indexedItems);
        _onCheckpoint(CloneStore(), CurrentStats());
    }

    private FileRecordStore CloneStore()
    {
        lock (_recordsGate)
        {
            var clone = new FileRecordStore
            {
                SourceKey = _store.SourceKey,
                SourceKind = _store.SourceKind,
                IdKind = _store.IdKind,
                RootId = _store.RootId,
                JournalId = _store.JournalId,
                NextUsn = _store.NextUsn
            };
            clone.Records.AddRange(_store.Records);
            return clone;
        }
    }

    private NetworkDriveWalkStats CurrentStats() => new NetworkDriveWalkStats(
            Volatile.Read(ref _skippedItems),
            Volatile.Read(ref _errors),
            Volatile.Read(ref _enumerateErrors),
            Volatile.Read(ref _attributeErrors),
            Volatile.Read(ref _reparseSkipped),
            Volatile.Read(ref _slowDirectories));

    private int GetWorkerCount() => _filter.WorkerCount > 0
            ? Math.Clamp(_filter.WorkerCount, 1, 32)
            : Math.Clamp(Environment.ProcessorCount, 2, 8);

    private void EnqueueDirectory(string path, string logicalPath, UInt128 parentId, int depth, NetworkIgnoreRuleSet ignoreRules)
    {
        Interlocked.Increment(ref _pendingDirectories);
        try
        {
            _pending.Writer.WriteAsync(new WorkItem(path, logicalPath, parentId, depth, ignoreRules), _token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            Interlocked.Decrement(ref _pendingDirectories);
            throw;
        }
    }

}
