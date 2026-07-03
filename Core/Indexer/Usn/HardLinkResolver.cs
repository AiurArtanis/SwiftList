using Microsoft.Win32.SafeHandles;

namespace SwiftList.Core.Indexer.Usn;

/// <summary>
/// Re-resolves a file reference number (FRN) to its current on-disk USN record. Used when a
/// USN_REASON_HARD_LINK_CHANGE arrives: that reason alone can't tell whether a hard link was
/// added or removed, and a file may still have other links, so we open the file by its FRN and
/// read its current primary name/parent. If the FRN no longer resolves, its last link is gone.
///
/// Note (P0 / issue #34): the index stores one name per FRN, so this only keeps that single
/// entry valid — it does not enumerate every hard link. Full multi-name support is separate.
/// </summary>
internal static class HardLinkResolver
{
    /// <summary>
    /// Returns the file's current USN record for <paramref name="frn"/> on <paramref name="drive"/>,
    /// or false when the file no longer exists (all hard links removed) / cannot be resolved.
    /// </summary>
    public static bool TryResolveRecord(string drive, UInt128 frn, out ParsedUsnRecord record)
    {
        record = default;
        SafeFileHandle? volume = null;
        SafeFileHandle? file = null;
        try
        {
            var share = Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE;

            // Any directory on the volume works as the OpenFileById frame; the drive root is simplest.
            // Must be opened with read access + backup semantics for it to be a valid volume hint.
            volume = Win32Api.CreateFileW($"{drive}:\\", Win32Api.GENERIC_READ, share, IntPtr.Zero,
                Win32Api.OPEN_EXISTING, Win32Api.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (volume.IsInvalid)
                return false;

            // Type must be ExtendedFileIdType (2) with dwSize 24; FileIdType (0) fails ERROR_INVALID_PARAMETER.
            // NTFS 64-bit FRNs go in the low 64 bits, high 64 zero.
            var desc = new Win32Api.FILE_ID_DESCRIPTOR
            {
                dwSize = 24,
                Type = 2,
                ExtendedFileId = new Win32Api.FILE_ID_128 { Low = (ulong)frn, High = (ulong)(frn >> 64) }
            };
            file = Win32Api.OpenFileById(volume, ref desc, Win32Api.GENERIC_READ, share, IntPtr.Zero,
                Win32Api.FILE_FLAG_BACKUP_SEMANTICS);
            if (file.IsInvalid)
                return false; // FRN no longer exists — the last hard link was removed.

            var outBuf = new byte[1024];
            if (!Win32Api.DeviceIoControl(file, Win32Api.FSCTL_READ_FILE_USN_DATA, IntPtr.Zero, 0,
                    outBuf, (uint)outBuf.Length, out var bytesReturned, IntPtr.Zero) || bytesReturned < 8)
                return false;

            var size = (int)bytesReturned;
            // The record normally starts at offset 0; be tolerant of a possible leading USN.
            foreach (var off in stackalloc[] { 0, 8 })
            {
                if (off + 8 > size) continue;
                var recLen = BitConverter.ToUInt32(outBuf, off);
                if (recLen < 56 || off + recLen > size) continue;
                try
                {
                    var parsed = UsnRecordParser.ParseRecord(new ReadOnlySpan<byte>(outBuf, off, (int)recLen));
                    if (parsed.FileReferenceNumber == frn && !string.IsNullOrEmpty(parsed.FileName))
                    {
                        record = parsed;
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            file?.Dispose();
            volume?.Dispose();
        }
    }
}
