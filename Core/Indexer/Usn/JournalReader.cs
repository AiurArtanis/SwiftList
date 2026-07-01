using System.Runtime.InteropServices;

namespace SwiftList.Core.Indexer.Usn;

public class JournalReader
{
    internal UsnDriveIndexResult? IndexDrive(string drive, Action<int, int>? onProgress = null)
    {
        Logger.Log($"[JournalReader] Indexing drive {drive}...");
        var volumePath = $"\\\\.\\{drive}:";
        using var handle = Win32Api.CreateFileW(
            volumePath,
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            Logger.Log($"[JournalReader] Failed to open drive {drive} handle.", LogLevel.Error);
            return null;
        }
        var fsType = VolumeHelper.GetFileSystemType(drive);
        var rootFrn = VolumeHelper.GetRootFrn(drive);
        if (!rootFrn.HasValue)
        {
            Logger.Log($"[JournalReader] Failed to resolve root FRN on {drive}.", LogLevel.Error);
            return null;
        }
        var queryBuf = new byte[56];
        var success = Win32Api.DeviceIoControl(
            handle,
            Win32Api.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            queryBuf, (uint)queryBuf.Length,
            out var bytesReturned,
            IntPtr.Zero);
        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            fsType = VolumeHelper.GetFileSystemType(drive);
            Logger.Log($"[JournalReader] Failed to query USN journal on {drive}. Error: {err}, FileSystem: {fsType}", LogLevel.Warn);

            if (fsType.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fsType.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"[JournalReader] Attempting to create/activate USN journal on {fsType} drive {drive}...");
                var createData = new Win32Api.CREATE_USN_JOURNAL_DATA
                {
                    MaximumSize = 0,
                    AllocationDelta = 0
                };
                var createSuccess = Win32Api.DeviceIoControl(
                    handle,
                    Win32Api.FSCTL_CREATE_USN_JOURNAL,
                    ref createData, (uint)Marshal.SizeOf<Win32Api.CREATE_USN_JOURNAL_DATA>(),
                    IntPtr.Zero, 0,
                    out var bytesReturnedCreate,
                    IntPtr.Zero);

                if (createSuccess)
                {
                    Logger.Log($"[JournalReader] USN journal successfully created/activated on {drive}. Retrying query...");
                    success = Win32Api.DeviceIoControl(
                        handle,
                        Win32Api.FSCTL_QUERY_USN_JOURNAL,
                        IntPtr.Zero, 0,
                        queryBuf, (uint)queryBuf.Length,
                        out bytesReturned,
                        IntPtr.Zero);
                }
                else
                {
                    var createErr = Marshal.GetLastWin32Error();
                    Logger.Log($"[JournalReader] Failed to create USN journal on {drive}. Error: {createErr}", LogLevel.Error);
                }
            }
        }

        if (!success)
        {
            Logger.Log($"[JournalReader] Failed to query USN journal on {drive}.", LogLevel.Error);
            return null;
        }
        var journalId = BitConverter.ToUInt64(queryBuf, 0);
        var nextUsn = BitConverter.ToInt64(queryBuf, 16);

        if (fsType.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
        {
            return ReFsScanner.ScanDrive(drive, handle, rootFrn.Value, journalId, nextUsn, onProgress);
        }

        var bufSize = 1024 * 1024;
        var outBuf = new byte[bufSize];
        ulong nextFrn = 0;
        var store = IndexCacheManager.CreateEmptyStore(drive, rootFrn.Value, nextUsn, journalId);
        store.Records.EnsureCapacity(2500000);
        var namePool = new FileRecordNamePool();
        var progress = new EnumerationProgress(onProgress);
        var loopCount = 0;

        while (true)
        {
            var input = new Win32Api.MFT_ENUM_DATA_V0
            {
                StartFileReferenceNumber = nextFrn,
                LowUsn = 0,
                HighUsn = nextUsn
            };

            var prevNextFrn = nextFrn;
            success = Win32Api.DeviceIoControl(
                handle,
                Win32Api.FSCTL_ENUM_USN_DATA,
                ref input, (uint)Marshal.SizeOf<Win32Api.MFT_ENUM_DATA_V0>(),
                outBuf, (uint)outBuf.Length,
                out bytesReturned,
                IntPtr.Zero
            );

            if (!success)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == Win32Api.ERROR_HANDLE_EOF)
                    break;

                Logger.Log($"[JournalReader] FSCTL_ENUM_USN_DATA on {drive} failed. Error: {err}", LogLevel.Error);
                break;
            }

            if (bytesReturned <= 8)
                break;

            nextFrn = BitConverter.ToUInt64(outBuf, 0);
            if (nextFrn == prevNextFrn)
                break;

            var offset = 8;
            var returnedSize = (int)bytesReturned;

            while (offset < returnedSize)
            {
                if (offset + 4 > returnedSize)
                    break;

                var recordLen = BitConverter.ToUInt32(outBuf, offset);
                if (recordLen == 0 || offset + recordLen > returnedSize)
                    break;

                var recordSpan = new ReadOnlySpan<byte>(outBuf, offset, (int)recordLen);
                try
                {
                    var record = UsnRecordParser.ParseRecord(recordSpan);
                    var flags = FileRecordFlagsHelper.FromAttributes((FileAttributes)record.FileAttributes);
 
                    store.Records.Add(new FileRecord(
                        record.FileReferenceNumber,
                        record.ParentFileReferenceNumber,
                        namePool.Get(record.FileName),
                        flags));
                    progress.Add(record.IsDirectory, store.Records.Count - 1);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[JournalReader] Record parsing error on {drive}: {ex}", LogLevel.Error);
                }

                offset += (int)recordLen;
            }

            loopCount++;
            if (loopCount % 60 == 0)
            {
                GC.Collect(1, GCCollectionMode.Forced, blocking: false);
            }
        }

        Logger.Log($"[JournalReader] Drive {drive} enum complete: {store.Records.Count - 1} items.");
        progress.Report();
        return new UsnDriveIndexResult
        {
            Store = store,
            NextUsn = nextUsn,
            JournalId = journalId,
            IsSortedById = true
        };
    }

    public long CatchUpDrive(string drive, ulong journalId, long startUsn, Action<ParsedUsnRecord> onRecord) => JournalReaderHelper.CatchUpDrive(drive, journalId, startUsn, onRecord);
}
