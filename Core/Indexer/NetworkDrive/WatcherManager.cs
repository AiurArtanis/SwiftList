namespace SwiftList.Core.Indexer.NetworkDrive;

internal class WatcherManager : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string, string> _queueRefresh;
    private readonly Func<string, NetworkIndex?> _getIndex;
    private readonly Action<string, NetworkIndex> _onIncrementalUpdate;
    private volatile bool _disposed;

    public WatcherManager(
        Action<string, string> queueRefresh,
        Func<string, NetworkIndex?> getIndex,
        Action<string, NetworkIndex> onIncrementalUpdate)
    {
        _queueRefresh = queueRefresh;
        _getIndex = getIndex;
        _onIncrementalUpdate = onIncrementalUpdate;
    }

    public void EnsureWatcher(string drive)
    {
        lock (_watchers)
        {
            if (_watchers.ContainsKey(drive))
                return;

            var root = drive + @":\";
            var physicalRoot = root;

            try
            {
                if (!Directory.Exists(physicalRoot))
                    return;

                var watcher = new FileSystemWatcher(physicalRoot)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.Attributes |
                                   NotifyFilters.CreationTime
                };

                FileSystemEventHandler onChanged = (_, e) => OnWatcherChanged(drive, e.ChangeType, e.FullPath);
                RenamedEventHandler onRenamed = (_, e) => OnWatcherRenamed(drive, e.OldFullPath, e.FullPath);
                ErrorEventHandler onError = (_, e) =>
                {
                    Logger.Log($"[WatcherManager] Watcher error on {drive}: {e.GetException().Message}", LogLevel.Error);
                    RemoveWatcher(drive);

                    if (_getIndex(drive) != null)
                    {
                        // Existing index is still valid; keep retrying until the watcher comes back up.
                        // ponytail: fixed 10 s back-off; upgrade to exponential if flapping becomes an issue.
                        _ = Task.Run(async () =>
                        {
                            while (!_disposed)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                                if (_disposed) break;
                                EnsureWatcher(drive);
                                lock (_watchers)
                                {
                                    if (_watchers.ContainsKey(drive)) break; // successfully re-watched
                                }
                            }
                        });
                    }
                    else
                    {
                        // No cached index — full rebuild needed.
                        _queueRefresh(drive, "watcher error");
                    }
                };

                watcher.Created += onChanged;
                watcher.Changed += onChanged;
                watcher.Deleted += onChanged;
                watcher.Renamed += onRenamed;
                watcher.Error += onError;
                watcher.EnableRaisingEvents = true;
                _watchers[drive] = watcher;
                Logger.Log($"[WatcherManager] Watching network drive {drive}: {physicalRoot}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[WatcherManager] Failed to watch network drive {drive}: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public void RemoveWatcher(string drive)
    {
        lock (_watchers)
        {
            if (_watchers.Remove(drive, out var watcher))
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    private string TranslateToLogical(string drive, string path) => path;

    private void OnWatcherChanged(string drive, WatcherChangeTypes changeType, string path)
    {
        try
        {
            var changed = false;
            var index = _getIndex(drive);

            if (index == null)
            {
                _queueRefresh(drive, "missing index");
                return;
            }

            var logicalPath = TranslateToLogical(drive, path);
            if (changeType == WatcherChangeTypes.Deleted)
            {
                changed = index.ApplyDeleted(logicalPath);
            }
            else
            {
                var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
                var isDirectory = Directory.Exists(path);
                if (exclusionRules.IsExcludedPath(logicalPath, isDirectory))
                    changed = index.ApplyDeleted(logicalPath);
                else
                    changed = index.ApplyCreatedOrChanged(drive + @":\", logicalPath, exclusionRules);
            }

            if (changed)
            {
                Logger.Log($"[WatcherManager] Incremental {changeType} applied on {drive}: {logicalPath}; items={index.Count}", LogLevel.Debug);
                _onIncrementalUpdate(drive, index);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WatcherManager] Watcher changed handling failed on {drive}: {ex.Message}", LogLevel.Error);
            _queueRefresh(drive, "incremental failure");
        }
    }

    private void OnWatcherRenamed(string drive, string oldPath, string newPath)
    {
        try
        {
            var index = _getIndex(drive);

            if (index == null)
            {
                _queueRefresh(drive, "missing index");
                return;
            }

            var logicalOldPath = TranslateToLogical(drive, oldPath);
            var logicalNewPath = TranslateToLogical(drive, newPath);
            var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
            var newIsDirectory = Directory.Exists(newPath);
            var changed = index.ApplyDeleted(logicalOldPath);
            if (!exclusionRules.IsExcludedPath(logicalNewPath, newIsDirectory))
                changed |= index.ApplyCreatedOrChanged(drive + @":\", logicalNewPath, exclusionRules);

            if (changed)
            {
                Logger.Log($"[WatcherManager] Incremental Rename applied on {drive}: {logicalOldPath} -> {logicalNewPath}; items={index.Count}", LogLevel.Debug);
                _onIncrementalUpdate(drive, index);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[WatcherManager] Watcher rename handling failed on {drive}: {ex.Message}", LogLevel.Error);
            _queueRefresh(drive, "incremental rename failure");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_watchers)
        {
            foreach (var watcher in _watchers.Values)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
        }
    }
}
