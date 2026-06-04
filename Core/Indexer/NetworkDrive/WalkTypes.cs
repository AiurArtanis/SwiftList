using System.IO;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal readonly record struct NetworkWalkRecord(FileRecord Record, FileAttributes Attributes)
    {
        public ulong Id => Record.Id;
        public ulong ParentId => Record.ParentId;
        public string Name => Record.Name;
        public FileRecordFlags Flags => Record.Flags;
        public FileAttributes Attributes { get; } = Attributes;

        public static implicit operator FileRecord(NetworkWalkRecord record) => record.Record;
    }

    internal enum WalkRecordResult
    {
        Success,
        AttributeError,
        ReparsePoint,
        InvalidName
    }

    internal readonly record struct WorkItem(string Path, string LogicalPath, ulong LocalId, int Depth, NetworkIgnoreRuleSet IgnoreRules);
}
