using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.Indexer.Usn;

public static class UsnIndexerBuildExtensions
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildIndex(this UsnIndexer indexer)
    {
        Logger.Log("[UsnIndexer] BuildIndex started");
        var drives = VolumeHelper.DetectIndexableLocalDrives();
        Logger.Log($"[UsnIndexer] Detected NTFS/ReFS drives: {string.Join(", ", drives)}");
        return indexer.BuildDrives(drives, clearExisting: true);
    }

    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        this UsnIndexer indexer,
        IReadOnlyList<string> drives,
        bool clearExisting,
        string? cacheDir = null)
    {
        lock (indexer.LockObj)
        {
            if (clearExisting)
            {
                indexer.Status.State = "indexing";
                indexer.Status.Progress = 0;
            }
            if (clearExisting)
            {
                indexer.Status.TotalFiles = 0;
                indexer.Status.TotalDirs = 0;
                indexer._driveMetadata.Clear();
                indexer._recordIndexes.Clear();
                indexer.Status.ActiveDrives.Clear();
            }

            if (clearExisting)
                indexer.Status.ActiveDrives = drives.ToList();
            else
            {
                foreach (var drive in drives)
                {
                    if (!indexer.Status.ActiveDrives.Contains(drive))
                        indexer.Status.ActiveDrives.Add(drive);
                    indexer.SetDriveState(drive, "indexing");
                }
            }
        }

        return IndexBuilder.BuildDrives(
            indexer._reader,
            drives,
            indexer.SetDriveState,
            indexer.UpdateDriveProgress,
            (drive, onProgress) => FolderDriveScanner.BuildStreaming(drive, onProgress, CancellationToken.None),
            (drive, result, progress, index) =>
            {
                indexer.DropDriveFromRuntime(drive);

                RuntimeIndex runtime;
                UsnIndexer.DriveRuntimeMetadata? metadata;

                if (!string.IsNullOrWhiteSpace(cacheDir))
                {
                    LocalDriveCacheLocator.Save(cacheDir, drive, result.Store);
                    result.Store.Records.Clear();
                    result.Store.Records.TrimExcess();

                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    Win32Api.TrimWorkingSet();

                    string? basePath = null;
                    try
                    {
                        var metaPath = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
                        if (!string.IsNullOrEmpty(metaPath) && metaPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            basePath = metaPath.Substring(0, metaPath.Length - 5);
                        }
                    }
                    catch {}

                    if (basePath != null)
                    {
                        runtime = new RuntimeIndex();
                        metadata = runtime.LoadFromCacheDirect(basePath);
                    }
                    else
                    {
                        runtime = new RuntimeIndex();
                        runtime.Load(result.Store);
                        metadata = UsnIndexer.CreateMetadata(result.Store);
                    }
                }
                else
                {
                    runtime = new RuntimeIndex();
                    runtime.Load(result.Store);
                    metadata = UsnIndexer.CreateMetadata(result.Store);
                }

                if (metadata != null)
                {
                    lock (indexer.LockObj)
                    {
                        indexer._driveMetadata[drive] = metadata;
                        indexer._recordIndexes[drive] = runtime;
                        indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
                        indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);
                        indexer.Status.Progress = progress;
                        indexer.UpdateDriveCounts(drive);
                    }
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Win32Api.TrimWorkingSet();
            },
            (drive, result, progress, index) =>
            {
                indexer.DropDriveFromRuntime(drive);

                RuntimeIndex runtime;
                UsnIndexer.DriveRuntimeMetadata? metadata;

                if (!string.IsNullOrWhiteSpace(cacheDir))
                {
                    LocalDriveCacheLocator.Save(cacheDir, drive, result.Store);
                    result.Store.Records.Clear();
                    result.Store.Records.TrimExcess();

                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    Win32Api.TrimWorkingSet();

                    string? basePath = null;
                    try
                    {
                        var metaPath = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
                        if (!string.IsNullOrEmpty(metaPath) && metaPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            basePath = metaPath.Substring(0, metaPath.Length - 5);
                        }
                    }
                    catch {}

                    if (basePath != null)
                    {
                        runtime = new RuntimeIndex();
                        metadata = runtime.LoadFromCacheDirect(basePath);
                    }
                    else
                    {
                        runtime = new RuntimeIndex();
                        runtime.Load(result.Store);
                        metadata = UsnIndexer.CreateMetadata(result.Store);
                    }
                }
                else
                {
                    runtime = new RuntimeIndex();
                    runtime.Load(result.Store);
                    metadata = UsnIndexer.CreateMetadata(result.Store);
                }

                if (metadata != null)
                {
                    lock (indexer.LockObj)
                    {
                        indexer._driveMetadata[drive] = metadata;
                        indexer._recordIndexes[drive] = runtime;
                        indexer.Status.TotalFiles = indexer._recordIndexes.Values.Sum(r => r.TotalFiles);
                        indexer.Status.TotalDirs = indexer._recordIndexes.Values.Sum(r => r.TotalDirs);

                        indexer.Status.Progress = progress;
                        indexer.UpdateDriveCounts(drive);
                    }
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Win32Api.TrimWorkingSet();
            },
            elapsedSeconds =>
            {
                lock (indexer.LockObj)
                {
                    indexer.Status.State = "ready";
                    indexer.Status.Progress = 100;
                    indexer.Status.ElapsedTime = elapsedSeconds;
                }
                Logger.Log($"[UsnIndexer] All indices built! Files: {indexer.Status.TotalFiles}, Folders: {indexer.Status.TotalDirs}. Time: {elapsedSeconds:F2}s");

                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Win32Api.TrimWorkingSet();
            }
        );
    }
}
