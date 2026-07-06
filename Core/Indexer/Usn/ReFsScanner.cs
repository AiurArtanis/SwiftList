using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace SwiftList.Core.Indexer.Usn;

// FILETIME-format timestamps (100ns since 1601-01-01 UTC), read straight out of the
// FILE_ID_EXTD_DIR_INFO buffer GetFileInformationByHandleEx already returns per entry.
internal readonly record struct ReFsItem(string Name, UInt128 ParentFrn, bool IsDir, long Size, long CreationTimeUtc, long LastWriteTimeUtc, long LastAccessTimeUtc);

public static class ReFsScanner
{
    internal static UsnDriveIndexResult? ScanDrive(
        string drive,
        SafeFileHandle volumeHandle,
        UInt128 rootFrn,
        ulong journalId,
        long nextUsn,
        Action<int, int>? onProgress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Log($"[ReFsScanner] Starting ReFS initial scan for drive {drive}...");

        // Slow path: parallel BFS via OpenFileById + GetFileInformationByHandleEx.
        // ponytail: O(N) I/O-bound scan; upgrade path = a documented ReFS full-enum API.
        Logger.Log($"[ReFsScanner] Drive {drive}: using ReFS directory-id BFS.");
        var items = ScanParallel(volumeHandle, rootFrn, onProgress);
        if (items == null)
            return null;

        stopwatch.Stop();
        var rate = stopwatch.Elapsed.TotalSeconds > 0 ? items.Count / stopwatch.Elapsed.TotalSeconds : items.Count;
        Logger.Log($"[ReFsScanner] Drive {drive}: directory-id BFS complete ({items.Count} items, {stopwatch.Elapsed.TotalSeconds:F2}s, {rate:F0} items/s).");
        return new UsnDriveIndexResult
        {
            Store = IndexCacheManager.CreateStoreFromDriveData(drive, rootFrn, items, nextUsn, journalId),
            NextUsn = nextUsn,
            JournalId = journalId,
            IsSortedById = false
        };
    }

    // Slow path: parallel BFS using Channel<UInt128> as the work queue.
    // Workers await new items (no spin); termination via channel.Writer.TryComplete() when inFlight hits 0.
    private static Dictionary<UInt128, ReFsItem>? ScanParallel(
        SafeFileHandle volumeHandle, UInt128 rootFrn, Action<int, int>? onProgress)
    {
        var items = new ConcurrentDictionary<UInt128, ReFsItem>(8, 32768);
        var channel = Channel.CreateUnbounded<UInt128>(new UnboundedChannelOptions { SingleReader = false });
        channel.Writer.TryWrite(rootFrn);
        var inFlight = 1;
        var files = 0;
        var dirs = 0;

        try
        {
            var workerCount = Math.Min(8, Environment.ProcessorCount);
            var tasks = Enumerable.Range(0, workerCount).Select(_ => Task.Run(async () =>
            {
                await foreach (var dirId in channel.Reader.ReadAllAsync())
                {
                    ProcessDir(volumeHandle, dirId, items, onProgress, ref files, ref dirs, subId =>
                    {
                        Interlocked.Increment(ref inFlight);
                        channel.Writer.TryWrite(subId);
                    });
                    // Only one thread sees 0; it completes the channel, ending all ReadAllAsync loops.
                    if (Interlocked.Decrement(ref inFlight) == 0)
                        channel.Writer.TryComplete();
                }
            })).ToArray();

            Task.WaitAll(tasks);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ReFsScanner] Parallel BFS error: {ex.Message}", LogLevel.Error);
            return null;
        }

        return new Dictionary<UInt128, ReFsItem>(items);
    }

    // Open one directory by file ID and enumerate its direct children.
    // Calls onSubdir for each subdirectory found (caller handles inFlight accounting).
    private static void ProcessDir(
        SafeFileHandle volumeHandle,
        UInt128 dirId,
        ConcurrentDictionary<UInt128, ReFsItem> items,
        Action<int, int>? onProgress,
        ref int files,
        ref int dirs,
        Action<UInt128> onSubdir)
    {
        var desc = new Win32Api.FILE_ID_DESCRIPTOR
        {
            dwSize = 24,
            Type = 2, // ExtendedFileIdType
            ExtendedFileId = new Win32Api.FILE_ID_128 { Low = (ulong)dirId, High = (ulong)(dirId >> 64) }
        };
        using var dirHandle = Win32Api.OpenFileById(volumeHandle, ref desc,
            1, Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | 4,
            IntPtr.Zero, Win32Api.FILE_FLAG_BACKUP_SEMANTICS);
        if (dirHandle.IsInvalid) return;

        const int bufSize = 1024 * 1024;
        var buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            // Loop until GetFileInformationByHandleEx returns false (ERROR_NO_MORE_FILES).
            // The original code only called it once, missing entries in large directories.
            while (Win32Api.GetFileInformationByHandleEx(dirHandle, Win32Api.FileIdExtdDirectoryInfo, buf, bufSize))
            {
                var cur = buf;
                while (true)
                {
                    var nextOff = (uint)Marshal.ReadInt32(cur, 0);
                    // FILE_ID_EXTD_DIR_INFO: already-fetched fields, no extra I/O to read them.
                    var creationTimeUtc = Marshal.ReadInt64(cur, 8);
                    var lastAccessTimeUtc = Marshal.ReadInt64(cur, 16);
                    var lastWriteTimeUtc = Marshal.ReadInt64(cur, 24);
                    var size = Marshal.ReadInt64(cur, 40);
                    var attrs = (uint)Marshal.ReadInt32(cur, 56);
                    var nameLen = (uint)Marshal.ReadInt32(cur, 60);
                    var idLow = (ulong)Marshal.ReadInt64(cur, 72);
                    var idHigh = (ulong)Marshal.ReadInt64(cur, 80);
                    var fileId = new UInt128(idHigh, idLow);
                    var name = Marshal.PtrToStringUni(cur + 88, (int)nameLen / 2);
                    if (name != "." && name != "..")
                    {
                        var isDir = (attrs & 0x10) != 0;
                        var item = new ReFsItem(name!, dirId, isDir, isDir ? 0 : size, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc);
                        if (items.TryAdd(fileId, item))
                        {
                            if (isDir)
                            {
                                Interlocked.Increment(ref dirs);
                                onSubdir(fileId);
                            }
                            else
                            {
                                Interlocked.Increment(ref files);
                            }

                            if ((items.Count & 4095) == 0)
                                onProgress?.Invoke(Volatile.Read(ref files), Volatile.Read(ref dirs));
                        }
                    }
                    if (nextOff == 0) break;
                    cur += (int)nextOff;
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
