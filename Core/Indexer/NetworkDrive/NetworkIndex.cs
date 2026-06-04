using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SwiftList.Core.SearchIndex.RecordIndex;
using SwiftList.Core.SearchIndex.RecordSearch;

namespace SwiftList.Core.Indexer.NetworkDrive
{
    internal sealed class NetworkIndex
    {
        private readonly object _gate = new();
        private readonly RuntimeIndex _runtime = new();
        private readonly Searcher _searcher = new();

        public NetworkIndex(string drive)
        {
            Drive = drive;
        }

        public string Drive { get; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public ulong RootId { get; private set; }
        public int Skipped { get; private set; }
        public int Errors { get; private set; }
        public int EnumerateErrors { get; private set; }
        public int AttributeErrors { get; private set; }
        public int ReparseSkipped { get; private set; }
        public int SlowDirectories { get; private set; }
        public int Count
        {
            get
            {
                lock (_gate)
                    return Math.Max(0, _runtime.TotalFiles + _runtime.TotalDirs - 1);
            }
        }

        public static NetworkIndex FromStore(FileRecordStore store)
        {
            var index = new NetworkIndex(store.SourceKey);
            index.RootId = store.RootId;
            lock (index._gate)
                index._runtime.Load(store);
            return index;
        }

        public static NetworkIndex FromStore(FileRecordStore store, NetworkDriveWalkStats stats)
        {
            var index = FromStore(store);
            index.Skipped = stats.Skipped;
            index.Errors = stats.Errors;
            index.EnumerateErrors = stats.EnumerateErrors;
            index.AttributeErrors = stats.AttributeErrors;
            index.ReparseSkipped = stats.ReparseSkipped;
            index.SlowDirectories = stats.SlowDirectories;
            index.LastUpdated = DateTime.Now;
            return index;
        }

        public static NetworkIndex Build(
            string drive,
            string root,
            string physicalRoot,
            WalkOptions options,
            CancellationToken token,
            Action<int> onProgress,
            Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null)
        {
            var index = new NetworkIndex(drive);
            const ulong rootId = 1;
            var store = new FileRecordStore
            {
                SourceKey = drive,
                SourceKind = FileRecordSourceKind.NetworkMappedDrive,
                IdKind = FileRecordIdKind.SourceLocalId64,
                RootId = rootId
            };
            store.Records.Add(new FileRecord(
                rootId,
                rootId,
                string.Empty,
                FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

            var builder = new TreeBuilder(store, root, physicalRoot, options, token, onProgress, onCheckpoint);
            var stats = builder.Run();

            index.RootId = rootId;
            index.Skipped = stats.Skipped;
            index.Errors = stats.Errors;
            index.EnumerateErrors = stats.EnumerateErrors;
            index.AttributeErrors = stats.AttributeErrors;
            index.ReparseSkipped = stats.ReparseSkipped;
            index.SlowDirectories = stats.SlowDirectories;
            index.LastUpdated = DateTime.Now;
            lock (index._gate)
                index._runtime.Load(store);
            onProgress(index.Count);
            return index;
        }

        public FileRecordStore ToStore()
        {
            lock (_gate)
            {
                return _runtime.ToStore(
                    FileRecordSourceKind.NetworkMappedDrive,
                    FileRecordIdKind.SourceLocalId64,
                    RootId,
                    journalId: 0,
                    nextUsn: 0);
            }
        }

        public void Search(ParsedSearchQuery parsed, string rawQuery, string? directoryFilterLower, int limit, List<SearchResult> results, CancellationToken token)
        {
            lock (_gate)
                results.AddRange(_searcher.Search(_runtime, rawQuery, limit, token, directoryFilterLower));
        }

        public bool ApplyCreatedOrChanged(string root, string path, ExclusionRuleSet? exclusionRules = null)
        {
            lock (_gate)
            {
                bool changed = UpsertPath(root, path, includeChildren: Directory.Exists(path), exclusionRules);
                if (changed)
                    LastUpdated = DateTime.Now;
                return changed;
            }
        }

        public bool ApplyDeleted(string path)
        {
            string normalized = PathHelpers.NormalizePath(path, isDirectory: false);
            ulong fileId = PathHelpers.HashPath64(normalized);
            string directoryNormalized = PathHelpers.NormalizePath(path, isDirectory: true);
            ulong directoryId = PathHelpers.HashPath64(directoryNormalized);

            lock (_gate)
            {
                bool removed = RemoveSubtree(fileId);
                if (directoryId != fileId)
                    removed |= RemoveSubtree(directoryId);

                if (removed)
                    LastUpdated = DateTime.Now;
                return removed;
            }
        }

        public bool ApplyRenamed(string root, string oldPath, string newPath, ExclusionRuleSet? exclusionRules = null)
        {
            lock (_gate)
            {
                bool changed = ApplyDeleted(oldPath);
                changed |= UpsertPath(root, newPath, includeChildren: Directory.Exists(newPath), exclusionRules);
                if (changed)
                    LastUpdated = DateTime.Now;
                return changed;
            }
        }

        private bool UpsertPath(string root, string path, bool includeChildren, ExclusionRuleSet? exclusionRules)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return false;

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (exclusionRules?.IsExcludedPath(path, isDirectory) == true)
                return ApplyDeleted(path);

            string normalized = PathHelpers.NormalizePath(path, isDirectory);
            string normalizedRoot = PathHelpers.NormalizePath(root, isDirectory: true);
            if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            string name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string? parentPath = Path.GetDirectoryName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            ulong parentId = string.IsNullOrWhiteSpace(parentPath) || PathHelpers.NormalizePath(parentPath, true).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? RootId
                : PathHelpers.HashPath64(PathHelpers.NormalizePath(parentPath, true));

            ulong id = PathHelpers.HashPath64(normalized);
            _runtime.Upsert(new FileRecord(
                id,
                parentId,
                name,
                isDirectory ? FileRecordFlags.Directory : FileRecordFlags.None));

            if (includeChildren && isDirectory)
                UpsertDirectoryChildren(root, normalized, exclusionRules);

            return true;
        }

        private void UpsertDirectoryChildren(string root, string directory, ExclusionRuleSet? exclusionRules)
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(directory);
            }
            catch
            {
                return;
            }

            foreach (string child in children)
                UpsertPath(root, child, includeChildren: true, exclusionRules);
        }

        private bool RemoveSubtree(ulong id)
        {
            var toRemove = new List<ulong>();
            CollectSubtree(id, toRemove);
            if (toRemove.Count == 0)
                return false;

            for (int i = toRemove.Count - 1; i >= 0; i--)
                _runtime.Remove(toRemove[i]);
            return true;
        }

        private void CollectSubtree(ulong id, List<ulong> ids)
        {
            foreach (int childIndex in _runtime.EnumerateChildren(id))
                CollectSubtree(_runtime.GetId(childIndex), ids);

            ids.Add(id);
        }
    }
}
