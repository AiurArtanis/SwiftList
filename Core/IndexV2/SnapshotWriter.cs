using System.Runtime.InteropServices;
using SwiftList.Core.Indexer.NetworkDrive;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.IndexV2;

// Builds a snapshot file straight from a scanner's FileRecordStore -- no RuntimeIndex intermediate.
// Soft-deleted records are folded away, survivors are re-sorted by id (stable by input order for
// hard-link ties), names dedupe into a UTF-8 arena with per-unique masks/aliases, and parent links
// resolve against the sorted id column; unresolved parents keep their true FRN in the orphan section
// so later updates can heal them, exactly like RuntimeIndex's orphan stash.
public static class SnapshotWriter
{
    public static void Write(FileRecordStore store, string path)
    {
        var records = store.Records;
        var order = new List<int>(records.Count);
        for (var i = 0; i < records.Count; i++)
            if (!records[i].IsDeleted)
                order.Add(i);
        order.Sort((a, b) =>
        {
            var c = records[a].Id.CompareTo(records[b].Id);
            return c != 0 ? c : a.CompareTo(b);
        });
        var count = order.Count;

        var nameIds = new uint[count];
        var flags = new ushort[count];
        var parentIndexes = new int[count];
        var ids = new UInt128[count];
        var sizes = new long[count];
        var creation = new uint[count];
        var lastWrite = new uint[count];
        var lastAccess = new uint[count];

        var uidByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameBlob = new MemoryStream();
        var nameOffsets = new List<uint> { 0 };
        var masks = new List<ulong>();
        var aliasStarts = new List<int>();
        var aliasEntryOffsets = new List<uint> { 0 };
        var aliasProviderIds = new List<byte>();
        var aliasBlob = new MemoryStream();
        var totalFiles = 0;
        var totalDirs = 0;

        for (var n = 0; n < count; n++)
        {
            var record = records[order[n]];
            if (!uidByName.TryGetValue(record.Name, out var uid))
            {
                uid = uidByName.Count;
                uidByName[record.Name] = uid;
                nameBlob.Write(SnapshotFormat.NameEncoding.GetBytes(record.Name));
                nameOffsets.Add(checked((uint)nameBlob.Length));

                aliasStarts.Add(aliasProviderIds.Count);
                var mask = FzfAlgorithm.GetCharMask(record.Name);
                var aliases = AliasGeneration.Generate(record.Name, out var providerIds);
                if (aliases != null)
                {
                    // Union of name + alias chars: over-admits, never rejects a real match -- the
                    // structural equivalent of the old engine bucketing rows under alias chars.
                    for (var a = 0; a < aliases.Length; a++)
                    {
                        aliasBlob.Write(SnapshotFormat.NameEncoding.GetBytes(aliases[a]));
                        aliasEntryOffsets.Add(checked((uint)aliasBlob.Length));
                        aliasProviderIds.Add(providerIds[a]);
                        mask |= FzfAlgorithm.GetCharMask(aliases[a]);
                    }
                }
                masks.Add(mask);
            }

            nameIds[n] = (uint)uid;
            flags[n] = (ushort)record.Flags;
            ids[n] = record.Id;
            sizes[n] = record.Size;
            creation[n] = record.CreationTimeUnixSeconds;
            lastWrite[n] = record.LastWriteTimeUnixSeconds;
            lastAccess[n] = record.LastAccessTimeUnixSeconds;
            if (record.IsDirectory)
                totalDirs++;
            else
                totalFiles++;
        }
        aliasStarts.Add(aliasProviderIds.Count);

        // Parent resolution against the sorted id column; unresolved links keep their FRN so a later
        // delta (or the next compaction) can heal them once the parent appears.
        var orphanRows = new List<int>();
        var orphanFrns = new List<UInt128>();
        for (var n = 0; n < count; n++)
        {
            var record = records[order[n]];
            var parentIndex = -1;
            if (record.ParentId != record.Id)
            {
                parentIndex = FirstRowForId(ids, record.ParentId);
                if (parentIndex < 0)
                {
                    orphanRows.Add(n);
                    orphanFrns.Add(record.ParentId);
                }
            }
            parentIndexes[n] = parentIndex;
        }

        var childStarts = new int[count + 1];
        for (var n = 0; n < count; n++)
            if (parentIndexes[n] >= 0)
                childStarts[parentIndexes[n] + 1]++;
        for (var n = 0; n < count; n++)
            childStarts[n + 1] += childStarts[n];
        var children = new int[childStarts[count]];
        var childCursor = (int[])childStarts.Clone();
        for (var n = 0; n < count; n++)
            if (parentIndexes[n] >= 0)
                children[childCursor[parentIndexes[n]]++] = n;

        var uidStarts = new int[uidByName.Count + 1];
        for (var n = 0; n < count; n++)
            uidStarts[nameIds[n] + 1]++;
        for (var n = 0; n < uidByName.Count; n++)
            uidStarts[n + 1] += uidStarts[n];
        var uidRows = new int[count];
        var uidCursor = (int[])uidStarts.Clone();
        for (var n = 0; n < count; n++)
            uidRows[uidCursor[nameIds[n]]++] = n;

        var meta = new SnapshotFormat.Meta
        {
            SourceKey = store.SourceKey,
            SourceRoot = PathHelpers.BuildSourceRoot(store.SourceKey),
            SourceKind = store.SourceKind,
            IdKind = store.IdKind,
            FileSystemType = store.FileSystemType,
            VolumeSerialNumber = store.VolumeSerialNumber,
            RootId = store.RootId,
            JournalId = store.JournalId,
            NextUsn = store.NextUsn,
            RowCount = count,
            UniqueCount = uidByName.Count,
            NameBlobLength = (int)nameBlob.Length,
            ChildrenLength = children.Length,
            AliasEntryCount = aliasProviderIds.Count,
            AliasBlobLength = (int)aliasBlob.Length,
            OrphanCount = orphanRows.Count,
            TotalFiles = totalFiles,
            TotalDirs = totalDirs,
            IsComplete = store.IsComplete,
            ExclusionRulesFingerprint = store.ExclusionRulesFingerprint,
            LastUpdated = store.LastUpdated,
        };

        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            using var writer = new BinaryWriter(stream, SnapshotFormat.NameEncoding, leaveOpen: true);
            SnapshotFormat.WriteHeader(writer, meta);
            SnapshotFormat.FinishHeader(stream, meta);
            var offsets = SnapshotFormat.ComputeSectionOffsets(meta, out var totalLength);

            WriteSection(stream, offsets, SnapshotSection.NameIds, MemoryMarshal.AsBytes<uint>(nameIds));
            WriteSection(stream, offsets, SnapshotSection.Flags, MemoryMarshal.AsBytes<ushort>(flags));
            WriteSection(stream, offsets, SnapshotSection.ParentIndexes, MemoryMarshal.AsBytes<int>(parentIndexes));
            WriteSection(stream, offsets, SnapshotSection.UniqueMasks, MemoryMarshal.AsBytes<ulong>(CollectionsMarshal.AsSpan(masks)));
            WriteSection(stream, offsets, SnapshotSection.NameOffsets, MemoryMarshal.AsBytes<uint>(CollectionsMarshal.AsSpan(nameOffsets)));
            WriteSection(stream, offsets, SnapshotSection.NameBlob, nameBlob.GetBuffer().AsSpan(0, (int)nameBlob.Length));
            WriteSection(stream, offsets, SnapshotSection.Ids, MemoryMarshal.AsBytes<UInt128>(ids));
            WriteSection(stream, offsets, SnapshotSection.Sizes, MemoryMarshal.AsBytes<long>(sizes));
            WriteSection(stream, offsets, SnapshotSection.CreationTimes, MemoryMarshal.AsBytes<uint>(creation));
            WriteSection(stream, offsets, SnapshotSection.LastWriteTimes, MemoryMarshal.AsBytes<uint>(lastWrite));
            WriteSection(stream, offsets, SnapshotSection.LastAccessTimes, MemoryMarshal.AsBytes<uint>(lastAccess));
            WriteSection(stream, offsets, SnapshotSection.ChildStarts, MemoryMarshal.AsBytes<int>(childStarts));
            WriteSection(stream, offsets, SnapshotSection.Children, MemoryMarshal.AsBytes<int>(children));
            WriteSection(stream, offsets, SnapshotSection.UidStarts, MemoryMarshal.AsBytes<int>(uidStarts));
            WriteSection(stream, offsets, SnapshotSection.UidRows, MemoryMarshal.AsBytes<int>(uidRows));
            WriteSection(stream, offsets, SnapshotSection.AliasStarts, MemoryMarshal.AsBytes<int>(CollectionsMarshal.AsSpan(aliasStarts)));
            WriteSection(stream, offsets, SnapshotSection.AliasEntryOffsets, MemoryMarshal.AsBytes<uint>(CollectionsMarshal.AsSpan(aliasEntryOffsets)));
            WriteSection(stream, offsets, SnapshotSection.AliasProviderIds, CollectionsMarshal.AsSpan(aliasProviderIds));
            WriteSection(stream, offsets, SnapshotSection.AliasBlob, aliasBlob.GetBuffer().AsSpan(0, (int)aliasBlob.Length));
            WriteSection(stream, offsets, SnapshotSection.OrphanRows, MemoryMarshal.AsBytes<int>(CollectionsMarshal.AsSpan(orphanRows)));
            WriteSection(stream, offsets, SnapshotSection.OrphanFrns, MemoryMarshal.AsBytes<UInt128>(CollectionsMarshal.AsSpan(orphanFrns)));
            stream.SetLength(totalLength);
        }

        FileRecordStoreReplaceHelper.ReplaceWithRetry(temp, path, TryDelete);
    }

    // First (lowest) row holding this id, or -1 -- hard-link duplicates sit adjacent after the sort.
    internal static int FirstRowForId(UInt128[] ids, UInt128 id)
    {
        int low = 0, high = ids.Length - 1, found = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            if (ids[mid] >= id)
            {
                if (ids[mid] == id)
                    found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return found;
    }

    private static void WriteSection(FileStream stream, long[] offsets, SnapshotSection section, ReadOnlySpan<byte> bytes)
    {
        stream.Position = offsets[(int)section];
        stream.Write(bytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
