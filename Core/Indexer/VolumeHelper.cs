using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core;

public static class VolumeHelper
{
    public readonly record struct VolumeIdentity(string FileSystemType, uint SerialNumber);

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
        var identity = GetVolumeIdentity(driveLetter);
        return identity?.FileSystemType ?? "NTFS";
    }

    public static VolumeIdentity? GetVolumeIdentity(string driveLetter)
    {
        var rootPath = $"{driveLetter}:\\";
        var volumeName = new StringBuilder(260);
        var fileSystemName = new StringBuilder(260);
        var success = Win32Api.GetVolumeInformationW(
            rootPath,
            volumeName, (uint)volumeName.Capacity,
            out var serial, out _, out _,
            fileSystemName, (uint)fileSystemName.Capacity
        );

        return success ? new VolumeIdentity(fileSystemName.ToString(), serial) : null;
    }

    public static string GetDisplayFileSystemType(string driveLetter)
    {
        var fsType = GetFileSystemType(driveLetter);
        return fsType switch
        {
            "NTFS" => $"NTFS {GetNtfsVersion(driveLetter)}",
            "ReFS" => $"ReFS {GetReFsVersion(driveLetter)}",
            _ => fsType
        };
    }

    public static string GetNtfsVersion(string driveLetter)
    {
        var volumePath = $"\\\\.\\{driveLetter}:";
        using var handle = Win32Api.CreateFileW(volumePath, Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero, Win32Api.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return "v?";

        // NTFS_VOLUME_DATA_BUFFER (96 bytes) + NTFS_EXTENDED_VOLUME_DATA.
        var ntfsBuf = new byte[128];
        if (Win32Api.DeviceIoControl(handle, Win32Api.FSCTL_GET_NTFS_VOLUME_DATA,
            IntPtr.Zero, 0, ntfsBuf, (uint)ntfsBuf.Length, out var returned, IntPtr.Zero)
            && returned >= 104)
        {
            var major = BitConverter.ToUInt16(ntfsBuf, 100);
            var minor = BitConverter.ToUInt16(ntfsBuf, 102);
            return $"v{major}.{minor}";
        }

        return "v?";
    }

    // Returns the ReFS on-disk format version string (e.g. "v3.14").
    public static string GetReFsVersion(string driveLetter)
    {
        var volumePath = $"\\\\.\\{driveLetter}:";
        using var handle = Win32Api.CreateFileW(volumePath, Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero, Win32Api.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return "v?";

        // REFS_VOLUME_DATA_BUFFER layout: [0] ByteCount(4), [4] MajorVersion(4), [8] MinorVersion(4), ...
        var refsBuf = new byte[512];
        if (Win32Api.DeviceIoControl(handle, Win32Api.FSCTL_GET_REFS_VOLUME_DATA,
            IntPtr.Zero, 0, refsBuf, (uint)refsBuf.Length, out _, IntPtr.Zero))
        {
            var major = BitConverter.ToUInt32(refsBuf, 4);
            var minor = BitConverter.ToUInt32(refsBuf, 8);
            return $"v{major}.{minor}";
        }

        return "v?";
    }
}
