using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core
{
    public class UsnMonitor
    {
        private readonly string _drive;
        private readonly ulong _journalId;
        private long _startUsn;
        private readonly UsnIndexer _indexer;
        private readonly CancellationToken _token;

        public UsnMonitor(string drive, ulong journalId, long startUsn, UsnIndexer indexer, CancellationToken token)
        {
            _drive = drive;
            _journalId = journalId;
            _startUsn = startUsn;
            _indexer = indexer;
            _token = token;
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                try
                {
                    await MonitorLoop();
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"[Monitor] Monitoring on drive {_drive} cancelled successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Monitor] Critical loop failure on drive {_drive}: {ex}");
                }
            }, _token);
        }

        private async Task MonitorLoop()
        {
            Logger.Log($"[Monitor] Started real-time monitoring on drive {_drive} from USN {_startUsn}...");
            string volumePath = $"\\\\.\\{_drive}:";
            
            using var handle = Win32Api.CreateFileW(
                volumePath,
                Win32Api.GENERIC_READ,
                Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32Api.OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if (handle.IsInvalid)
            {
                Logger.Log($"[Monitor] Failed to open drive {_drive} handle for monitoring.");
                return;
            }

            int bufSize = 64 * 1024;
            byte[] outBuf = new byte[bufSize];
            int consecutiveEmptyReads = 0;

            while (!_token.IsCancellationRequested)
            {
                try
                {
                    long previousUsn = _startUsn;

                    var input = new Win32Api.READ_USN_JOURNAL_DATA_V0
                    {
                        StartUsn = _startUsn,
                        ReasonMask = 0xFFFFFFFF, // All reasons
                        ReturnOnlyOnClose = 0,
                        Timeout = 0,
                        BytesToWaitFor = 0,
                        UsnJournalID = _journalId
                    };

                    uint bytesReturned;
                    bool success = Win32Api.DeviceIoControl(
                        handle,
                        Win32Api.FSCTL_READ_USN_JOURNAL,
                        ref input, (uint)Marshal.SizeOf<Win32Api.READ_USN_JOURNAL_DATA_V0>(),
                        outBuf, (uint)outBuf.Length,
                        out bytesReturned,
                        IntPtr.Zero
                    );

                    if (!success)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Logger.Log($"[Monitor] FSCTL_READ_USN_JOURNAL error on drive {_drive}: {err}");
                        // Don't immediately break; wait and retry a few times
                        await Task.Delay(2000, _token);
                        consecutiveEmptyReads++;
                        if (consecutiveEmptyReads > 10)
                        {
                            Logger.Log($"[Monitor] Too many consecutive errors on drive {_drive}. Stopping monitor.");
                            break;
                        }
                        continue;
                    }

                    consecutiveEmptyReads = 0;
                    int returnedSize = (int)bytesReturned;

                    if (returnedSize > 8)
                    {
                        _startUsn = BitConverter.ToInt64(outBuf, 0);
                        int offset = 8;
                        int recordsProcessed = 0;
                        var records = new List<Win32Api.ParsedUsnRecord>();

                        while (offset < returnedSize)
                        {
                            if (offset + 4 > returnedSize)
                                break;

                            uint recordLen = BitConverter.ToUInt32(outBuf, offset);
                            if (recordLen == 0 || offset + recordLen > returnedSize)
                                break;

                            ReadOnlySpan<byte> recordSpan = new ReadOnlySpan<byte>(outBuf, offset, (int)recordLen);
                            try
                            {
                                records.Add(Win32Api.ParseRecord(recordSpan));
                                recordsProcessed++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"[Monitor] Record parse error during monitoring on {_drive}: {ex}");
                            }

                            offset += (int)recordLen;
                        }

                        if (records.Count > 0)
                            _indexer.ApplyUsnRecords(_drive, records);

                        // If USN didn't advance, we're stuck — delay to prevent spin loop
                        if (_startUsn == previousUsn)
                        {
                            await Task.Delay(1000, _token);
                        }
                        else if (recordsProcessed == 0)
                        {
                            // USN advanced but no meaningful records — short delay
                            await Task.Delay(200, _token);
                        }
                        // else: processed records and USN advanced — continue immediately
                    }
                    else
                    {
                        if (returnedSize == 8)
                        {
                            _startUsn = BitConverter.ToInt64(outBuf, 0);
                        }
                        
                        // No changes, await to avoid CPU spinning
                        await Task.Delay(500, _token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Monitor] Unexpected error in monitor loop on drive {_drive}: {ex}");
                    await Task.Delay(2000, _token);
                }
            }

            Logger.Log($"[Monitor] Stopped monitoring on drive {_drive}.");
        }
    }
}
