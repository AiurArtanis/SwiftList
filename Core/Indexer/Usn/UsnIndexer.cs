using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.Indexer.Shared;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public class UsnIndexer : IDisposable
{
    public event Action<IndexerStatus>? StatusChanged;
    private long _lastProgressPublishTicks;

    public class IndexerStatus
    {
        public string State { get; set; } = "idle";
        public int Progress { get; set; } = 0;
        public int TotalFiles { get; set; } = 0;
        public int TotalDirs { get; set; } = 0;
        public double ElapsedTime { get; set; } = 0.0;
        public bool IsMaintenanceBusy { get; set; }
        public List<string> ActiveDrives { get; set; } = new();
        public List<DriveIndexStatus> Drives { get; set; } = new();
    }

    public class DriveIndexStatus
    {
        public string Drive { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Kind { get; set; } = "LocalNtfs";
        public string State { get; set; } = "unknown";
        public int Files { get; set; }
        public int Dirs { get; set; }
        public string CachePath { get; set; } = string.Empty;
    }

    internal readonly object _lockObj = new();
    internal readonly JournalReader _reader = new();
    internal readonly Dictionary<string, DriveRuntimeMetadata> _driveMetadata = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, RuntimeIndex> _recordIndexes = new(StringComparer.OrdinalIgnoreCase);

    public IndexerStatus Status { get; } = new();
    public object LockObj => _lockObj;

    internal sealed class DriveRuntimeMetadata
    {
        public FileRecordSourceKind SourceKind { get; init; }
        public FileRecordIdKind IdKind { get; init; }
        public string FileSystemType { get; init; } = string.Empty;
        public uint VolumeSerialNumber { get; init; }
        public UInt128 RootId { get; init; }
        public ulong JournalId { get; set; }
        public long NextUsn { get; set; }
    }


    public void SearchStreaming(string query, int limit, Action<SearchResult> onResult, CancellationToken token = default, string? directoryFilter = null) => SearchCoordinator.SearchStreaming(_recordIndexes, LockObj, query, limit, onResult, token, directoryFilter);

    public void SetDriveStatuses(IEnumerable<DriveIndexStatus> drives)
    {
        lock (LockObj)
        {
            Status.Drives = drives.ToList();
        }
        PublishStatusChanged();
    }

    public void SetDriveState(string drive, string state) => SetDriveState(drive, state, false);

    public void SetDriveState(string drive, string state, bool resetCounts)
    {
        lock (LockObj)
        {
            var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            item.State = state;
            if (resetCounts)
            {
                item.Files = 0;
                item.Dirs = 0;
            }
        }
        PublishStatusChanged();
    }

    public void UpdateDriveProgress(string drive, int files, int dirs)
    {
        lock (LockObj)
        {
            var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            item.State = "indexing";
            item.Files = files;
            item.Dirs = dirs;
            if (Status.ActiveDrives.Count == 1 && Status.ActiveDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                Status.Progress = Math.Min(95, Status.Progress + 1);
            else
                Status.Progress = Math.Min(99, Math.Max(Status.Progress, 1));
        }
        NotifyProgressChanged();
    }

    public long CatchUpDrive(string drive, ulong journalId, long startUsn)
    {
        var changes = new List<ParsedUsnRecord>();
        var nextUsn = _reader.CatchUpDrive(drive, journalId, startUsn, changes.Add);
        if (nextUsn >= 0 && changes.Count > 0)
            ApplyUsnRecords(drive, changes);

        return nextUsn;
    }

    public void ApplyUsnRecord(string drive, ParsedUsnRecord record) => ApplyUsnRecords(drive, new[] { record });

    public void ApplyUsnRecords(string drive, IReadOnlyList<ParsedUsnRecord> records)
    {
        Logger.Log($"[UsnIndexer] Applying {records.Count} USN records to drive {drive}", LogLevel.Debug);
        lock (LockObj)
        {
            if (!_recordIndexes.TryGetValue(drive, out var runtime))
                return;
            var namePool = new FileRecordNamePool();

            foreach (var record in records)
            {
                if ((record.Reason & (Win32Api.USN_REASON_FILE_DELETE | Win32Api.USN_REASON_RENAME_OLD_NAME)) != 0)
                {
                    runtime.Remove(ToSourceLocalId(record.FileReferenceNumber));
                    continue;
                }

                if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) == 0)
                    continue;

                var flags = record.IsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None;
                var fileRecord = new FileRecord(
                    ToSourceLocalId(record.FileReferenceNumber),
                    ToSourceLocalId(record.ParentFileReferenceNumber),
                    namePool.Get(record.FileName),
                    flags);

                runtime.Upsert(fileRecord);
            }

            UpdateTotalsFromRuntime();
            UpdateDriveCounts(drive);
            SearchCoordinator.ClearCaches();
        }
        PublishStatusChanged();
    }

    public void ApplyFolderChange(string drive, WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        lock (LockObj)
        {
            if (!_recordIndexes.TryGetValue(drive, out var runtime))
                return;

            var root = $"{drive}:\\";
            var normalizedPath = PathHelpers.NormalizePath(path, Directory.Exists(path));
            var changed = false;

            changed = changeType switch
            {
                WatcherChangeTypes.Deleted => PathDeltaApplier.ApplyDeleted(runtime, normalizedPath),
                WatcherChangeTypes.Renamed when !string.IsNullOrWhiteSpace(oldPath) => PathDeltaApplier.ApplyRenamed(runtime, (UInt128)1, root, oldPath, normalizedPath),
                _ => PathDeltaApplier.ApplyCreatedOrChanged(runtime, (UInt128)1, root, normalizedPath),
            };
            if (!changed)
                return;

            UpdateTotalsFromRuntime();
            UpdateDriveCounts(drive);
            SearchCoordinator.ClearCaches();
            SaveDriveSnapshot(drive, runtime);
        }
        PublishStatusChanged();
    }

    private void SaveDriveSnapshot(string drive, RuntimeIndex runtime)
    {
        if (!_driveMetadata.TryGetValue(drive, out var metadata))
            return;

        var store = runtime.ToStore(
            metadata.SourceKind,
            metadata.IdKind,
            metadata.FileSystemType,
            metadata.VolumeSerialNumber,
            metadata.RootId,
            metadata.JournalId,
            metadata.NextUsn);
        LocalDriveCacheLocator.Save(Path.Combine(Logger.UserDataDir, "indexes"), drive, store);
    }

    public void CompactMemory()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            Win32Api.TrimWorkingSet();
        }
        catch { }
    }

    public void ClearCaches() => SearchCoordinator.ClearCaches();

    public void CompactStatusQueryMemory()
    {
        ClearCaches();
        CompactMemory();
    }

    public void UnloadRuntime()
    {
        lock (LockObj)
        {
            _driveMetadata.Clear();
            _recordIndexes.Clear();
            Status.ActiveDrives.Clear();
            Status.TotalFiles = Status.Drives.Sum(d => d.Files);
            Status.TotalDirs = Status.Drives.Sum(d => d.Dirs);
            if (Status.State == "ready")
                Status.State = "idle";
        }
        PublishStatusChanged();
    }

    internal static DriveRuntimeMetadata CreateMetadata(FileRecordStore store) => new DriveRuntimeMetadata
    {
        SourceKind = store.SourceKind,
        IdKind = store.IdKind,
        FileSystemType = store.FileSystemType,
        VolumeSerialNumber = store.VolumeSerialNumber,
        RootId = store.RootId,
        JournalId = store.JournalId,
        NextUsn = store.NextUsn
    };

    private static UInt128 ToSourceLocalId(UInt128 value) => value;

    private void UpdateTotalsFromRuntime()
    {
        Status.TotalFiles = _recordIndexes.Values.Sum(r => r.TotalFiles);
        Status.TotalDirs = _recordIndexes.Values.Sum(r => r.TotalDirs);
    }

    internal void UpdateDriveCounts(string drive)
    {
        var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        if (_recordIndexes.TryGetValue(drive, out var runtime))
        {
            item.Files = runtime.TotalFiles;
            item.Dirs = runtime.TotalDirs;
        }
        item.State = "ready";
    }

    public void Dispose()
    {
        _driveMetadata.Clear();
        _recordIndexes.Clear();
    }

    public IndexerStatus SnapshotStatus()
    {
        lock (LockObj)
        {
            return new IndexerStatus
            {
                State = Status.State,
                Progress = Status.Progress,
                TotalFiles = Status.TotalFiles,
                TotalDirs = Status.TotalDirs,
                ElapsedTime = Status.ElapsedTime,
                IsMaintenanceBusy = Status.IsMaintenanceBusy,
                ActiveDrives = Status.ActiveDrives.ToList(),
                Drives = Status.Drives.Select(d => new DriveIndexStatus
                {
                    Drive = d.Drive,
                    Enabled = d.Enabled,
                    Kind = d.Kind,
                    State = d.State,
                    Files = d.Files,
                    Dirs = d.Dirs,
                    CachePath = d.CachePath
                }).ToList()
            };
        }
    }

    private void PublishStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(SnapshotStatus());
        }
        catch
        {
        }
    }

    public void NotifyStatusChanged() => PublishStatusChanged();

    public void NotifyProgressChanged()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressPublishTicks);
        if (now - last < 100)
            return;

        Interlocked.Exchange(ref _lastProgressPublishTicks, now);
        PublishStatusChanged();
    }
}
