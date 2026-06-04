using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SwiftList.Core
{
    public static class Win32Api
    {
        // ==========================================
        // Win32 Constants
        // ==========================================
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1;
        public const uint FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const IntPtr INVALID_HANDLE_VALUE = -1;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
        public const uint FSCTL_ENUM_USN_DATA = 0x000900b3;
        public const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;
        public const uint ERROR_HANDLE_EOF = 38;

        // USN Reason Codes
        public const uint USN_REASON_FILE_CREATE = 0x00000100;
        public const uint USN_REASON_FILE_DELETE = 0x00000200;
        public const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
        public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;

        // ==========================================
        // Win32 Structures
        // ==========================================
        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint dwFileAttributes;
            public ulong ftCreationTime;
            public ulong ftLastAccessTime;
            public ulong ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_ID_128
        {
            public ulong Low;
            public ulong High;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_ID_INFO
        {
            public ulong VolumeSerialNumber;
            public FILE_ID_128 FileId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct USN_JOURNAL_DATA_V0
        {
            public ulong UsnJournalID;
            public long FirstUsn;
            public long NextUsn;
            public long LowestValidUsn;
            public long MaxUsn;
            public ulong MaximumSize;
            public ulong AllocationDelta;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MFT_ENUM_DATA_V0
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct READ_USN_JOURNAL_DATA_V0
        {
            public long StartUsn;
            public uint ReasonMask;
            public uint ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public ulong UsnJournalID;
        }

        public struct ParsedUsnRecord
        {
            public uint RecordLength;
            public ushort MajorVersion;
            public UInt128 FileReferenceNumber;
            public UInt128 ParentFileReferenceNumber;
            public long Usn;
            public uint Reason;
            public uint FileAttributes;
            public string FileName;
            public bool IsDirectory => (FileAttributes & 0x00000010) != 0; // FILE_ATTRIBUTE_DIRECTORY
        }

        // ==========================================
        // Win32 API Imports
        // ==========================================
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref MFT_ENUM_DATA_V0 lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetVolumeInformationW(
            string lpRootPathName,
            StringBuilder lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer,
            uint nFileSystemNameSize
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            int FileInformationClass,
            out FILE_ID_INFO lpFileInformation,
            uint dwBufferSize
        );

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr ShellExecuteW(
            IntPtr hwnd,
            string lpOperation,
            string lpFile,
            string lpParameters,
            string lpDirectory,
            int nShowCmd
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }

        // ==========================================
        // USN Record Parser using Span
        // ==========================================
        public static ParsedUsnRecord ParseRecord(ReadOnlySpan<byte> span)
        {
            var record = new ParsedUsnRecord();
            record.RecordLength = MemoryMarshal.Read<uint>(span.Slice(0, 4));
            record.MajorVersion = MemoryMarshal.Read<ushort>(span.Slice(4, 2));

            ushort nameLength = 0;
            ushort nameOffset = 0;

            if (record.MajorVersion == 2)
            {
                ulong frn = MemoryMarshal.Read<ulong>(span.Slice(8, 8));
                ulong parentFrn = MemoryMarshal.Read<ulong>(span.Slice(16, 8));
                record.FileReferenceNumber = frn;
                record.ParentFileReferenceNumber = parentFrn;
                record.Usn = MemoryMarshal.Read<long>(span.Slice(24, 8));
                record.Reason = MemoryMarshal.Read<uint>(span.Slice(40, 4));
                record.FileAttributes = MemoryMarshal.Read<uint>(span.Slice(52, 4));
                nameLength = MemoryMarshal.Read<ushort>(span.Slice(56, 2));
                nameOffset = MemoryMarshal.Read<ushort>(span.Slice(58, 2));
            }
            else if (record.MajorVersion == 3)
            {
                ulong frnLow = MemoryMarshal.Read<ulong>(span.Slice(8, 8));
                ulong frnHigh = MemoryMarshal.Read<ulong>(span.Slice(16, 8));
                ulong parentLow = MemoryMarshal.Read<ulong>(span.Slice(24, 8));
                ulong parentHigh = MemoryMarshal.Read<ulong>(span.Slice(32, 8));

                record.FileReferenceNumber = new UInt128(frnHigh, frnLow);
                record.ParentFileReferenceNumber = new UInt128(parentHigh, parentLow);
                record.Usn = MemoryMarshal.Read<long>(span.Slice(40, 8));
                record.Reason = MemoryMarshal.Read<uint>(span.Slice(56, 4));
                record.FileAttributes = MemoryMarshal.Read<uint>(span.Slice(68, 4));
                nameLength = MemoryMarshal.Read<ushort>(span.Slice(72, 2));
                nameOffset = MemoryMarshal.Read<ushort>(span.Slice(74, 2));
            }
            else
            {
                throw new NotSupportedException($"USN Record Major Version {record.MajorVersion} is not supported.");
            }

            if (nameLength > 0 && nameOffset > 0 && nameOffset + nameLength <= record.RecordLength)
            {
                ReadOnlySpan<byte> nameSpan = span.Slice(nameOffset, nameLength);
                record.FileName = Encoding.Unicode.GetString(nameSpan);
            }
            else
            {
                record.FileName = string.Empty;
            }

            return record;
        }
    }
}
