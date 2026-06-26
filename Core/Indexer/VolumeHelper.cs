using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core;

public static class VolumeHelper
{
    public static UInt128? GetRootFrn(string driveLetter)
    {
        var path = $"{driveLetter}:\\";
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
            if (Win32Api.GetFileInformationByHandleEx(handle, 18, out var info, (uint)Marshal.SizeOf<Win32Api.FILE_ID_INFO>()))
            {
                return new UInt128(info.FileId.High, info.FileId.Low);
            }
        }
        catch
        {
            // Fall back
        }

        if (Win32Api.GetFileInformationByHandle(handle, out var stdInfo))
        {
            var frn = ((ulong)stdInfo.nFileIndexHigh << 32) | stdInfo.nFileIndexLow;
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
            var driveLetter = drive.Name.Split(':')[0].ToUpper();

            var volumeName = new StringBuilder(260);
            var fileSystemName = new StringBuilder(260);

            var success = Win32Api.GetVolumeInformationW(
                drive.Name,
                volumeName, (uint)volumeName.Capacity,
                out _, out _, out _,
                fileSystemName, (uint)fileSystemName.Capacity
            );

            if (success)
            {
                var fs = fileSystemName.ToString();
                if (fs == "NTFS" || fs == "ReFS")
                {
                    detected.Add(driveLetter);
                }
            }
        }
        return detected;
    }

    public static List<string> DetectFolderIndexDrives()
    {
        var detected = new List<string>();
        var drives = DriveInfo.GetDrives();
        foreach (var drive in drives)
        {
            if (!drive.IsReady) continue;
            var driveLetter = drive.Name.Split(':')[0].ToUpper();

            var volumeName = new StringBuilder(260);
            var fileSystemName = new StringBuilder(260);
            var success = Win32Api.GetVolumeInformationW(
                drive.Name,
                volumeName, (uint)volumeName.Capacity,
                out _, out _, out _,
                fileSystemName, (uint)fileSystemName.Capacity
            );

            if (success)
            {
                var fs = fileSystemName.ToString();
                if (fs != "NTFS" && fs != "ReFS")
                {
                    detected.Add(driveLetter);
                }
            }
        }
        return detected;
    }

    public static string GetFileSystemType(string driveLetter)
    {
        var rootPath = $"{driveLetter}:\\";
        var volumeName = new StringBuilder(260);
        var fileSystemName = new StringBuilder(260);

        var success = Win32Api.GetVolumeInformationW(
            rootPath,
            volumeName, (uint)volumeName.Capacity,
            out _, out _, out _,
            fileSystemName, (uint)fileSystemName.Capacity
        );

        return success ? fileSystemName.ToString() : "NTFS";
    }

    // Probe whether a ReFS volume uses v3.x on-disk format (128-bit FRNs).
    // v3.x: formatted on Win10 1803+ / Server 2019+ → FSCTL_ENUM_USN_DATA V1 succeeds.
    // v1.x: older format → FSCTL returns ERROR_INVALID_FUNCTION or ERROR_NOT_SUPPORTED.
    public static string GetReFsVersion(string driveLetter)
    {
        var volumePath = $"\\\\.\\{driveLetter}:";
        using var handle = Win32Api.CreateFileW(volumePath, Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero, Win32Api.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return "v?";

        var input = new Win32Api.MFT_ENUM_DATA_V1 { HighUsn = long.MaxValue, MinMajorVersion = 3, MaxMajorVersion = 3 };
        var outBuf = new byte[8];
        var ok = Win32Api.DeviceIoControl(handle, Win32Api.FSCTL_ENUM_USN_DATA,
            ref input, (uint)Marshal.SizeOf<Win32Api.MFT_ENUM_DATA_V1>(),
            outBuf, (uint)outBuf.Length, out _, IntPtr.Zero);
        var err = Marshal.GetLastWin32Error();
        // ERROR_HANDLE_EOF = probe accepted, no records; any other success = records returned.
        return (ok || err == Win32Api.ERROR_HANDLE_EOF) ? "v3" : "v1";
    }
}
