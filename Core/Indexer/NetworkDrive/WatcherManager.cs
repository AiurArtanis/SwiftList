using System;
using System.Collections.Generic;
using System.IO;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal class WatcherManager : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, string> _queueRefresh;
        private readonly Func<string, NetworkIndex?> _getIndex;
        private readonly Action<string, NetworkIndex> _onIncrementalUpdate;

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

                string root = drive + @":\";
                string physicalRoot = root;
                if (!Directory.Exists(root))
                {
                    string? uncPath = NetworkDriveResolver.ResolveToUnc(drive);
                    if (!string.IsNullOrEmpty(uncPath))
                    {
                        physicalRoot = uncPath;
                    }
                }

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
                        Logger.Log($"[WatcherManager] Watcher error on {drive}: {e.GetException().Message}", SwiftList.Core.LogLevel.Error);
                        _queueRefresh(drive, "watcher error");
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
                    Logger.Log($"[WatcherManager] Failed to watch network drive {drive}: {ex.Message}", SwiftList.Core.LogLevel.Error);
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

        private string TranslateToLogical(string drive, string path)
        {
            string root = drive + @":\";
            string? uncPath = NetworkDriveResolver.ResolveToUnc(drive);
            if (!string.IsNullOrEmpty(uncPath) && path.StartsWith(uncPath, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(root, path.Substring(uncPath.Length).TrimStart(Path.DirectorySeparatorChar));
            }
            return path;
        }

        private void OnWatcherChanged(string drive, WatcherChangeTypes changeType, string path)
        {
            try
            {
                bool changed = false;
                NetworkIndex? index = _getIndex(drive);

                if (index == null)
                {
                    _queueRefresh(drive, "missing index");
                    return;
                }

                string logicalPath = TranslateToLogical(drive, path);
                if (changeType == WatcherChangeTypes.Deleted)
                {
                    changed = index.ApplyDeleted(logicalPath);
                }
                else
                {
                    var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
                    bool isDirectory = Directory.Exists(path);
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
                Logger.Log($"[WatcherManager] Watcher changed handling failed on {drive}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                _queueRefresh(drive, "incremental failure");
            }
        }

        private void OnWatcherRenamed(string drive, string oldPath, string newPath)
        {
            try
            {
                NetworkIndex? index = _getIndex(drive);

                if (index == null)
                {
                    _queueRefresh(drive, "missing index");
                    return;
                }

                string logicalOldPath = TranslateToLogical(drive, oldPath);
                string logicalNewPath = TranslateToLogical(drive, newPath);
                var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
                bool newIsDirectory = Directory.Exists(newPath);
                bool changed = index.ApplyDeleted(logicalOldPath);
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
                Logger.Log($"[WatcherManager] Watcher rename handling failed on {drive}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                _queueRefresh(drive, "incremental rename failure");
            }
        }

        public void Dispose()
        {
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
}
