namespace SwiftList.Core;

internal sealed class FolderDriveMonitor : IDisposable
{
    private readonly string _drive;
    private readonly Action<string> _queueRebuild;
    private readonly CancellationToken _token;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public FolderDriveMonitor(string drive, Action<string> queueRebuild, CancellationToken token)
    {
        _drive = drive;
        _queueRebuild = queueRebuild;
        _token = token;
    }

    public void Start()
    {
        var root = $"{_drive}:\\";
        if (!Directory.Exists(root))
            return;

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        FileSystemEventHandler changed = (_, _) => Schedule();
        RenamedEventHandler renamed = (_, _) => Schedule();
        _watcher.Created += changed;
        _watcher.Changed += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += renamed;
        _watcher.Error += (_, e) => { Logger.Log($"[FolderDriveMonitor] Watcher error on {_drive}: {e.GetException().Message}", LogLevel.Warn); Schedule(); };
        _watcher.EnableRaisingEvents = true;
        Logger.Log($"[FolderDriveMonitor] Started monitoring {_drive}: via FileSystemWatcher.");
    }

    private void Schedule()
    {
        if (_token.IsCancellationRequested)
            return;

        _debounce?.Dispose();
        _debounce = new Timer(_ => _queueRebuild(_drive), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _debounce?.Dispose();
        _watcher?.Dispose();
    }
}
