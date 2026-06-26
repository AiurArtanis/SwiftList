using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace SwiftList.Core.Indexer.Usn;

public static class ReFsScanner
{
    public static (UInt128 RootFrn,
            Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)> SearchItems,
            long NextUsn, ulong JournalId)? ScanDrive(
        string drive,
        SafeFileHandle volumeHandle,
        UInt128 rootFrn,
        ulong journalId,
        long nextUsn)
    {
        Logger.Log($"[ReFsScanner] Starting ReFS initial scan for drive {drive}...");

        // Fast path: FSCTL_ENUM_USN_DATA V1 requesting USN_RECORD_V3 (128-bit FRNs).
        // Supported on Win10 1809+ / Server 2019+. Falls back to BFS on older systems.
        var items = TryFastEnum(volumeHandle, nextUsn);
        if (items != null)
        {
            Logger.Log($"[ReFsScanner] Drive {drive}: fast enum complete ({items.Count} items).");
            return (rootFrn, items, nextUsn, journalId);
        }

        // Slow path: parallel BFS via OpenFileById + GetFileInformationByHandleEx.
        // ponytail: O(N) I/O-bound scan; upgrade path = fast enum on future ReFS/Windows versions.
        Logger.Log($"[ReFsScanner] Drive {drive}: fast enum unavailable, using parallel BFS.");
        items = ScanParallel(volumeHandle, rootFrn);
        if (items == null)
            return null;

        Logger.Log($"[ReFsScanner] Drive {drive}: parallel BFS complete ({items.Count} items).");
        return (rootFrn, items, nextUsn, journalId);
    }

    // Fast path: enumerate all file records via FSCTL_ENUM_USN_DATA V1.
    // Returns null if not supported by this volume/OS (caller falls back to BFS).
    private static Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)>? TryFastEnum(
        SafeFileHandle volumeHandle, long nextUsn)
    {
        const int bufSize = 1024 * 1024;
        var outBuf = new byte[bufSize];
        var items = new Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)>(32768);
        ulong nextFrn = 0;

        while (true)
        {
            var input = new Win32Api.MFT_ENUM_DATA_V1
            {
                StartFileReferenceNumber = nextFrn,
                LowUsn = 0,
                HighUsn = nextUsn,
                MinMajorVersion = 3, // request USN_RECORD_V3 (128-bit FRNs)
                MaxMajorVersion = 3
            };

            var ok = Win32Api.DeviceIoControl(
                volumeHandle, Win32Api.FSCTL_ENUM_USN_DATA,
                ref input, (uint)Marshal.SizeOf<Win32Api.MFT_ENUM_DATA_V1>(),
                outBuf, (uint)outBuf.Length,
                out var returned, IntPtr.Zero);

            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == Win32Api.ERROR_HANDLE_EOF)
                    break; // enumeration complete

                // ERROR_INVALID_FUNCTION (1) or ERROR_NOT_SUPPORTED (50) = not available on this volume
                Logger.Log($"[ReFsScanner] FSCTL_ENUM_USN_DATA V1 not supported (err={err})", LogLevel.Debug);
                return null;
            }

            if (returned <= 8) break;

            var next = BitConverter.ToUInt64(outBuf, 0); // next StartFileReferenceNumber
            if (next == nextFrn) break;
            nextFrn = next;

            var offset = 8;
            var end = (int)returned;
            while (offset < end)
            {
                if (offset + 4 > end) break;
                var recLen = BitConverter.ToUInt32(outBuf, offset);
                if (recLen == 0 || offset + recLen > end) break;
                try
                {
                    var rec = UsnRecordParser.ParseRecord(new ReadOnlySpan<byte>(outBuf, offset, (int)recLen));
                    if (!string.IsNullOrEmpty(rec.FileName))
                        items[rec.FileReferenceNumber] = (rec.FileName, rec.ParentFileReferenceNumber, rec.IsDirectory);
                }
                catch { }
                offset += (int)recLen;
            }
        }

        return items;
    }

    // Slow path: parallel BFS using Channel<UInt128> as the work queue.
    // Workers await new items (no spin); termination via channel.Writer.TryComplete() when inFlight hits 0.
    private static Dictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)>? ScanParallel(
        SafeFileHandle volumeHandle, UInt128 rootFrn)
    {
        var items = new ConcurrentDictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)>(8, 32768);
        var channel = Channel.CreateUnbounded<UInt128>(new UnboundedChannelOptions { SingleReader = false });
        channel.Writer.TryWrite(rootFrn);
        var inFlight = 1;

        try
        {
            var workerCount = Math.Min(8, Environment.ProcessorCount);
            var tasks = Enumerable.Range(0, workerCount).Select(_ => Task.Run(async () =>
            {
                await foreach (var dirId in channel.Reader.ReadAllAsync())
                {
                    ProcessDir(volumeHandle, dirId, items, subId =>
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

        return new Dictionary<UInt128, (string, UInt128, bool)>(items);
    }

    // Open one directory by file ID and enumerate its direct children.
    // Calls onSubdir for each subdirectory found (caller handles inFlight accounting).
    private static void ProcessDir(
        SafeFileHandle volumeHandle,
        UInt128 dirId,
        ConcurrentDictionary<UInt128, (string Name, UInt128 ParentFrn, bool IsDir)> items,
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
                    var attrs = (uint)Marshal.ReadInt32(cur, 56);
                    var nameLen = (uint)Marshal.ReadInt32(cur, 60);
                    var idLow = (ulong)Marshal.ReadInt64(cur, 72);
                    var idHigh = (ulong)Marshal.ReadInt64(cur, 80);
                    var fileId = new UInt128(idHigh, idLow);
                    var name = Marshal.PtrToStringUni(cur + 88, (int)nameLen / 2);
                    if (name != "." && name != "..")
                    {
                        var isDir = (attrs & 0x10) != 0;
                        items[fileId] = (name!, dirId, isDir);
                        if (isDir) onSubdir(fileId);
                    }
                    if (nextOff == 0) break;
                    cur += (int)nextOff;
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
