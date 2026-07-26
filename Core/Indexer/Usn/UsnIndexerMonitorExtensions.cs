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

    // Stops (without replacing) whatever monitor is currently registered for this one drive, if any --
    // called right before a single-drive rebuild starts. A rebuild replaces this drive's LiveIndex
    // wholesale once it finishes (see UsnIndexerBuildExtensions.OnDriveCompleted's DropDriveFromRuntime),
    // discarding whatever in-memory delta a still-running old monitor had applied to the doomed old
    // instance in the meantime -- for a non-journaled drive (FolderDriveMonitor), that delta was the ONLY
    // record of any change detected during the rebuild, so it's gone for good once the old LiveIndex is
    // disposed. Stopping the monitor before the rebuild starts instead means the fresh walk itself is the
    // sole (and, since it reads live filesystem state as it walks, generally sufficient) source of truth
    // for that window; the new monitor DriveMonitorFactory.EnsureMonitor registers once the rebuild
    // finishes picks up everything from that point on. A USN-journal drive doesn't need this (its next
    // monitor replays from the pre-scan watermark regardless -- see JournalReader.IndexDrive), but
    // stopping it early here is harmless either way.
    internal static void RemoveDriveMonitor(this UsnIndexer indexer, string drive)
    {
        IDisposable? old;
        lock (indexer.LockObj)
            indexer._driveMonitors.Remove(drive, out old);
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
