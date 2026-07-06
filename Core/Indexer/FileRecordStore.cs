namespace SwiftList.Core;

public enum FileRecordSourceKind : byte
{
    LocalMft = 1,
    NetworkMappedDrive = 2
}

public enum FileRecordIdKind : byte
{
    MftFrn = 1,
    SourceLocalId64 = 2
}

[Flags]
public enum FileRecordFlags : ushort
{
    None = 0,
    Directory = 1,
    Deleted = 2,
    SourceRoot = 4,
    Hidden = 8,
    System = 16,
    ReadOnly = 32,
    Compressed = 64,
    Encrypted = 128
}

public static class FileRecordFlagsHelper
{
    public static FileRecordFlags FromAttributes(FileAttributes attrs)
    {
        var flags = FileRecordFlags.None;
        if ((attrs & FileAttributes.Directory) != 0) flags |= FileRecordFlags.Directory;
        if ((attrs & FileAttributes.Hidden) != 0) flags |= FileRecordFlags.Hidden;
        if ((attrs & FileAttributes.System) != 0) flags |= FileRecordFlags.System;
        if ((attrs & FileAttributes.ReadOnly) != 0) flags |= FileRecordFlags.ReadOnly;
        if ((attrs & FileAttributes.Compressed) != 0) flags |= FileRecordFlags.Compressed;
        if ((attrs & FileAttributes.Encrypted) != 0) flags |= FileRecordFlags.Encrypted;
        return flags;
    }

    public static FileAttributes ToAttributes(FileRecordFlags flags)
    {
        var attrs = (FileAttributes)0;
        if ((flags & FileRecordFlags.Directory) != 0) attrs |= FileAttributes.Directory;
        if ((flags & FileRecordFlags.Hidden) != 0) attrs |= FileAttributes.Hidden;
        if ((flags & FileRecordFlags.System) != 0) attrs |= FileAttributes.System;
        if ((flags & FileRecordFlags.ReadOnly) != 0) attrs |= FileAttributes.ReadOnly;
        if ((flags & FileRecordFlags.Compressed) != 0) attrs |= FileAttributes.Compressed;
        if ((flags & FileRecordFlags.Encrypted) != 0) attrs |= FileAttributes.Encrypted;
        if (attrs == 0) attrs = FileAttributes.Normal;
        return attrs;
    }
}

public readonly struct FileRecord
{
    public FileRecord(
        UInt128 id,
        UInt128 parentId,
        string name,
        FileRecordFlags flags,
        long size = 0,
        long creationTimeUtc = 0,
        long lastWriteTimeUtc = 0,
        long lastAccessTimeUtc = 0)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        Flags = flags;
        Size = size;
        CreationTimeUtc = creationTimeUtc;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LastAccessTimeUtc = lastAccessTimeUtc;
    }

    public UInt128 Id { get; }
    public UInt128 ParentId { get; }
    public string Name { get; }
    public FileRecordFlags Flags { get; }
    // Logical (apparent) size in bytes. Always 0 for directories.
    public long Size { get; }
    // FILETIME format (100ns intervals since 1601-01-01 UTC) -- the native representation for both
    // NTFS $STANDARD_INFORMATION and Win32's GetFileAttributesEx, so every source can store its raw
    // value with no conversion. Use DateTime.FromFileTimeUtc to convert for display.
    public long CreationTimeUtc { get; }
    public long LastWriteTimeUtc { get; }
    public long LastAccessTimeUtc { get; }
    public bool IsDirectory => (Flags & FileRecordFlags.Directory) != 0;
    public bool IsDeleted => (Flags & FileRecordFlags.Deleted) != 0;
}

public sealed class FileRecordStore
{
    public string SourceKey { get; set; } = string.Empty;
    public FileRecordSourceKind SourceKind { get; set; }
    public FileRecordIdKind IdKind { get; set; }
    public string FileSystemType { get; set; } = string.Empty;
    public uint VolumeSerialNumber { get; set; }
    public UInt128 RootId { get; set; }
    public ulong JournalId { get; set; }
    public long NextUsn { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<FileRecord> Records { get; } = new();
}

public readonly record struct FileRecordStoreSummary(
    string SourceKey,
    FileRecordSourceKind SourceKind,
    FileRecordIdKind IdKind,
    string FileSystemType,
    uint VolumeSerialNumber,
    UInt128 RootId,
    ulong JournalId,
    long NextUsn,
    int RecordCount,
    int LiveRecordCount,
    DateTime LastUpdated);
