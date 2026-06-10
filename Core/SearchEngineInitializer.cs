using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core
{
    internal class SearchEngineInitializer
    {
        private readonly UsnIndexer _indexer;
        private readonly StartMenuAppIndex _appIndex;
        private readonly string _indexCacheDir;

        public SearchEngineInitializer(UsnIndexer indexer, StartMenuAppIndex appIndex, string indexCacheDir)
        {
            _indexer = indexer;
            _appIndex = appIndex;
            _indexCacheDir = indexCacheDir;
        }

        public void EnsureDriveStatuses(IReadOnlyList<string> detectedDrives, IReadOnlyList<string> enabledDrives)
        {
            var enabled = new HashSet<string>(enabledDrives, StringComparer.OrdinalIgnoreCase);
            var statuses = detectedDrives.Select(d => new UsnIndexer.DriveIndexStatus
            {
                Drive = d,
                Enabled = enabled.Contains(d),
                Kind = VolumeHelper.GetFileSystemType(d),
                State = enabled.Contains(d) ? "pending" : "disabled",
                CachePath = FileRecordStoreSerializer.GetBasePath(_indexCacheDir, d) + ".meta"
            }).ToList();

            _indexer.SetDriveStatuses(statuses);
        }

        public void Run(bool forceRebuild, CancellationTokenSource cts, Action<bool> onComplete)
        {
            try
            {
                _appIndex.Refresh();

                var machineSettings = MachineSettings.Load();
                var detectedDrives = VolumeHelper.DetectSupportedDrives();
                var enabledSet = new HashSet<string>(machineSettings.EnabledLocalDrives, StringComparer.OrdinalIgnoreCase);
                var supportedDrives = enabledSet.Count == 0
                    ? detectedDrives
                    : detectedDrives.Where(enabledSet.Contains).ToList();

                EnsureDriveStatuses(detectedDrives, supportedDrives);

                bool loadedFromCache = false;
                List<(string Drive, ulong JournalId, long NextUsn)> cachedMetadata = new();

                if (!forceRebuild)
                {
                    Logger.Log("[SearchEngineInitializer] Attempting to load per-drive index caches...");
                    lock (_indexer.LockObj)
                    {
                        _indexer.Status.State = "loading-cache";
                        _indexer.Status.Progress = 0;
                    }

                    cachedMetadata = _indexer.LoadDrivesFromCache(_indexCacheDir, supportedDrives);
                    if (cachedMetadata.Count > 0)
                    {
                        Logger.Log($"[SearchEngineInitializer] Loaded {cachedMetadata.Count}/{supportedDrives.Count} per-drive caches. Instantly unblocking UI for instant search.");

                        lock (_indexer.LockObj)
                        {
                            _indexer.Status.State = "ready";
                            _indexer.Status.Progress = 100;
                            _indexer.Status.ActiveDrives = cachedMetadata.Select(m => m.Drive).ToList();
                        }

                        loadedFromCache = true;
                    }
                    else
                    {
                        Logger.Log("[SearchEngineInitializer] No per-drive caches loaded. Falling back to full scan.", SwiftList.Core.LogLevel.Warn);
                    }
                }

                var monitorsToStart = new List<(string Drive, ulong JournalId, long NextUsn)>();

                if (loadedFromCache)
                {
                    // Catch up from the cached USN silently in background
                    bool catchUpSuccess = true;
                    var updatedMetadata = new List<(string Drive, ulong JournalId, long NextUsn)>();

                    for (int i = 0; i < cachedMetadata.Count; i++)
                    {
                        var meta = cachedMetadata[i];
                        long newUsn = _indexer.CatchUpDrive(meta.Drive, meta.JournalId, meta.NextUsn);

                        if (newUsn < 0)
                        {
                            Logger.Log($"[SearchEngineInitializer] Silent catch-up failed for drive {meta.Drive} (journal mismatch or error). Requiring full re-index.", SwiftList.Core.LogLevel.Error);
                            catchUpSuccess = false;
                            break;
                        }

                        updatedMetadata.Add((meta.Drive, meta.JournalId, newUsn));
                    }

                    if (catchUpSuccess)
                    {
                        Logger.Log("[SearchEngineInitializer] Silent background catch-up completed successfully.");
                        monitorsToStart.AddRange(updatedMetadata);
                        _indexer.SaveDrivesToCache(_indexCacheDir, updatedMetadata);

                        var loadedDrives = new HashSet<string>(updatedMetadata.Select(m => m.Drive), StringComparer.OrdinalIgnoreCase);
                        var missingDrives = supportedDrives.Where(d => !loadedDrives.Contains(d)).ToList();
                        if (missingDrives.Count > 0)
                        {
                            Logger.Log($"[SearchEngineInitializer] Building missing per-drive indices: {string.Join(", ", missingDrives)}");
                            var missingMetadata = _indexer.BuildDrives(missingDrives, clearExisting: false);
                            monitorsToStart.AddRange(missingMetadata);
                            _indexer.SaveDrivesToCache(_indexCacheDir, missingMetadata);
                        }
                    }
                    else
                    {
                        // Fallback to full reindex
                        loadedFromCache = false;
                        lock (_indexer.LockObj)
                        {
                            _indexer.Status.State = "indexing";
                            _indexer.Status.Progress = 0;
                        }
                    }
                }

                if (!loadedFromCache)
                {
                    Logger.Log("[SearchEngineInitializer] Building new index from scratch...");
                    var newMetadata = _indexer.BuildDrives(supportedDrives, clearExisting: true);
                    monitorsToStart = newMetadata;

                    // Persist the completed index for the next startup.
                    _indexer.SaveDrivesToCache(_indexCacheDir, newMetadata);
                }

                _indexer.CompactMemory();

                var monitorDrives = new HashSet<string>(monitorsToStart.Select(m => m.Drive), StringComparer.OrdinalIgnoreCase);
                foreach (var drive in supportedDrives)
                {
                    if (!monitorDrives.Contains(drive))
                        _indexer.SetDriveState(drive, "failed");
                }

                foreach (var (drive, journalId, nextUsn) in monitorsToStart)
                {
                    var monitor = new UsnMonitor(drive, journalId, nextUsn, _indexer, cts.Token);
                    monitor.Start();
                }

                Logger.Log($"[SearchEngineInitializer] Started real-time USN monitors for {monitorsToStart.Count} drives.");
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchEngineInitializer] Index initialization failed: {ex}", SwiftList.Core.LogLevel.Error);
            }
            finally
            {
                onComplete(false);
            }
        }
    }
}
