namespace SwiftList.Core.Indexer.Usn;

// Drive-monitor lifecycle for UsnIndexer, as extension methods (matching UsnIndexerExtensions/
// UsnIndexerCacheExtensions/UsnIndexerBuildExtensions' own split) to keep UsnIndexer.cs under the
// project's line limit.
internal static class UsnIndexerMonitorExtensions
{
    // Stops and replaces whatever monitor was previously registered for this drive, if any -- called
    // exactly once per monitor start, from DriveMonitorFactory.EnsureMonitor. Disposed outside the lock:
    // FolderDriveMonitor.Dispose() tears down a real FileSystemWatcher, and a CancellationDisposable's
    // Cancel() can run arbitrary continuations -- neither should happen while holding LockObj.
    internal static void RegisterDriveMonitor(this UsnIndexer indexer, string drive, IDisposable monitor)
    {
        IDisposable? old;
        lock (indexer.LockObj)
        {
            indexer._driveMonitors.TryGetValue(drive, out old);
            indexer._driveMonitors[drive] = monitor;
        }
        old?.Dispose();
    }

    // Stops every currently-registered monitor -- a full rebuild-from-scratch tearing down and restarting
    // everything, or final app shutdown.
    internal static void DisposeAllDriveMonitors(this UsnIndexer indexer)
    {
        List<IDisposable> monitors;
        lock (indexer.LockObj)
        {
            monitors = indexer._driveMonitors.Values.ToList();
            indexer._driveMonitors.Clear();
        }
        foreach (var monitor in monitors)
            monitor.Dispose();
    }
}
