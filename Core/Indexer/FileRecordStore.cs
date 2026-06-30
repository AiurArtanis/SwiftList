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
    SourceRoot = 4
}

public readonly struct FileRecord
{
    public FileRecord(
        UInt128 id,
        UInt128 parentId,
        string name,
        FileRecordFlags flags)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        Flags = flags;
    }

    public UInt128 Id { get; }
    public UInt128 ParentId { get; }
    public string Name { get; }
    public FileRecordFlags Flags { get; }
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
