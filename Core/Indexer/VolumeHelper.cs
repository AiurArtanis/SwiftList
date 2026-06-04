using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core
{
    public static class VolumeHelper
    {
        public static UInt128? GetRootFrn(string driveLetter)
        {
            string path = $"{driveLetter}:\\";
            using var handle = Win32Api.CreateFileW(
                path,
                0,
                Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32Api.OPEN_EXISTING,
                Win32Api.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero
            );

            if (handle.IsInvalid)
                return null;

            try
            {
                if (Win32Api.GetFileInformationByHandleEx(handle, 18, out Win32Api.FILE_ID_INFO info, (uint)Marshal.SizeOf<Win32Api.FILE_ID_INFO>()))
                {
                    return new UInt128(info.FileId.High, info.FileId.Low);
                }
            }
            catch
            {
                // Fall back
            }

            if (Win32Api.GetFileInformationByHandle(handle, out Win32Api.BY_HANDLE_FILE_INFORMATION stdInfo))
            {
                ulong frn = ((ulong)stdInfo.nFileIndexHigh << 32) | stdInfo.nFileIndexLow;
                return frn;
            }

            return null;
        }

        public static List<string> DetectSupportedDrives()
        {
            var detected = new List<string>();
            var drives = DriveInfo.GetDrives();

            foreach (var drive in drives)
            {
                if (!drive.IsReady) continue;
                string driveLetter = drive.Name.Split(':')[0].ToUpper();
                
                var volumeName = new StringBuilder(260);
                var fileSystemName = new StringBuilder(260);
                
                bool success = Win32Api.GetVolumeInformationW(
                    drive.Name,
                    volumeName, (uint)volumeName.Capacity,
                    out _, out _, out _,
                    fileSystemName, (uint)fileSystemName.Capacity
                );

                if (success)
                {
                    string fs = fileSystemName.ToString();
                    if (fs == "NTFS" || fs == "ReFS")
                    {
                        detected.Add(driveLetter);
                    }
                }
            }
            return detected;
        }
    }
}
