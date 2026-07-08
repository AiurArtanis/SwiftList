using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class UpdateExtensions
{
    public static void Upsert(this RuntimeIndex index, FileRecord record)
    {
        var name = record.Name;

        if (index.TryGetIndexById(record.Id, out var oldIndex))
        {
            var newParentIndex = index.ResolveParentIndex(record.Id, record.ParentId);
            var oldParentIndex = index.ParentIndexes[oldIndex];
            if (oldParentIndex != newParentIndex)
            {
                if (oldParentIndex >= 0 && index.ParentToChildren.TryGetValue(oldParentIndex, out var oldList))
                {
                    oldList.Remove(oldIndex);
                }
                if (newParentIndex >= 0)
                {
                    if (!index.ParentToChildren.TryGetValue(newParentIndex, out var newList))
                    {
                        newList = new List<int>();
                        index.ParentToChildren[newParentIndex] = newList;
                    }
                    newList.Add(oldIndex);
                }
            }

            var oldIsDirectory = index.IsDirectory(oldIndex);

            index.ParentIndexes[oldIndex] = newParentIndex;
            index.TrackOrphanParent(oldIndex, newParentIndex, record.ParentId);
            index.NameIds[oldIndex] = index.Names.GetId(name);
            index.Flags[oldIndex] = (ushort)record.Flags;
            index.Sizes[oldIndex] = record.Size;
            index.CreationTimes[oldIndex] = record.CreationTimeUnixSeconds;
            index.LastWriteTimes[oldIndex] = record.LastWriteTimeUnixSeconds;
            index.LastAccessTimes[oldIndex] = record.LastAccessTimeUnixSeconds;
            var newAliases = index.GenerateAliases(name, out var newProviderIds);
            if (newAliases != null && newAliases.Length > 0)
            {
                index.DeltaNameAliases[oldIndex] = newAliases;
                index.DeltaAliasProviderIds[oldIndex] = newProviderIds;
                if (oldIndex < index.LoadedCount)
                {
                    index.HasAlias.Set(oldIndex, true);
                }
                index.CharMasks[oldIndex] = ulong.MaxValue;
            }
            else
            {
                index.DeltaNameAliases[oldIndex] = Array.Empty<string>();
                index.DeltaAliasProviderIds[oldIndex] = Array.Empty<byte>();
                if (oldIndex < index.LoadedCount)
                {
                    index.HasAlias.Set(oldIndex, false);
                }
                index.CharMasks[oldIndex] = (((FileRecordFlags)index.Flags[oldIndex]) & FileRecordFlags.Deleted) != 0
                    ? 0
                    : FzfAlgorithm.GetCharMask(name);
            }

            index.AddNameCharDelta(name, oldIndex);

            if (oldIsDirectory != record.IsDirectory)
            {
                if (oldIsDirectory)
                {
                    index.TotalDirs--;
                    index.TotalFiles++;
                }
                else
                {
                    index.TotalFiles--;
                    index.TotalDirs++;
                }
            }

            index.PathMemo.Clear();
            return;
        }

        var idx = index.Count;
        index.AddColumns(
            record.Id,
            name,
            record.Flags,
            record.Size,
            record.CreationTimeUnixSeconds,
            record.LastWriteTimeUnixSeconds,
            record.LastAccessTimeUnixSeconds);
        index.DeltaIdToIndex[record.Id] = idx;

        var parentIndex = index.ResolveParentIndex(record.Id, record.ParentId);
        index.ParentIndexes.Add(parentIndex);
        index.TrackOrphanParent(idx, parentIndex, record.ParentId);
        if (parentIndex >= 0)
        {
            if (!index.ParentToChildren.TryGetValue(parentIndex, out var newList))
            {
                newList = new List<int>();
                index.ParentToChildren[parentIndex] = newList;
            }
            newList.Add(idx);
        }

        // This new record may be the parent that earlier out-of-order rows were orphaned waiting for.
        index.ReparentWaitingOrphans(idx, record.Id);

        if (record.IsDirectory)
            index.TotalDirs++;
        else
            index.TotalFiles++;

        var addedAliases = index.GenerateAliases(name, out var addedProviderIds);
        if (addedAliases != null && addedAliases.Length > 0)
        {
            index.DeltaNameAliases[idx] = addedAliases;
            index.DeltaAliasProviderIds[idx] = addedProviderIds;
            index.CharMasks[idx] = ulong.MaxValue;
        }
        else if (record.IsDeleted)
        {
            index.CharMasks[idx] = 0;
        }

        index.AddNameCharDelta(name, idx);
        index.PathMemo.Clear();
    }

    public static void Remove(this RuntimeIndex index, UInt128 id)
    {
        if (!index.TryGetIndexById(id, out var idx))
            return;

        var wasDirectory = index.IsDirectory(idx);
        index.Flags[idx] = (ushort)(((FileRecordFlags)index.Flags[idx]) | FileRecordFlags.Deleted);
        index.CharMasks[idx] = 0;

        index.DeltaIdToIndex.Remove(id);
        index.DeltaNameAliases.Remove(idx);
        index.DeltaAliasProviderIds.Remove(idx);
        index.OrphanParentFrns.TryRemove(idx, out _); // keep the orphan table mirroring the row lifecycle

        var parentIndex = index.ParentIndexes[idx];
        if (parentIndex >= 0 && index.ParentToChildren.TryGetValue(parentIndex, out var list))
        {
            list.Remove(idx);
        }

        if (wasDirectory)
            index.TotalDirs = Math.Max(0, index.TotalDirs - 1);
        else
            index.TotalFiles = Math.Max(0, index.TotalFiles - 1);

        index.PathMemo.Clear();
    }


    internal static void EnsureCapacity(this RuntimeIndex index, int capacity)
    {
        index.Ids.Capacity = Math.Max(index.Ids.Capacity, capacity);
        index.ParentIndexes.Capacity = Math.Max(index.ParentIndexes.Capacity, capacity);
        index.NameIds.Capacity = Math.Max(index.NameIds.Capacity, capacity);
        index.Flags.Capacity = Math.Max(index.Flags.Capacity, capacity);
        index.CharMasks.Capacity = Math.Max(index.CharMasks.Capacity, capacity);
        index.Sizes.Capacity = Math.Max(index.Sizes.Capacity, capacity);
        index.CreationTimes.Capacity = Math.Max(index.CreationTimes.Capacity, capacity);
        index.LastWriteTimes.Capacity = Math.Max(index.LastWriteTimes.Capacity, capacity);
        index.LastAccessTimes.Capacity = Math.Max(index.LastAccessTimes.Capacity, capacity);
    }

    // A parent FRN that earlier out-of-order rows were orphaned waiting for just appeared at newRow: link
    // those orphans now so directory-children queries include them too (their paths already resolve via
    // the stash; this also restores ParentToChildren). Runs under the index write lock.
    // ponytail: linear scan of the (rare) orphan set per new record — ceiling O(orphans) per insert; add
    // a reverse parentFrn->rows map if orphan counts ever grow large.
    internal static void ReparentWaitingOrphans(this RuntimeIndex index, int newRow, UInt128 frn)
    {
        // Only a directory can legitimately be the parent orphans are waiting for. Without this, a FRN
        // that used to belong to a directory (deleted, or mid-rename with a never-arriving new-name
        // record) can be reused by an unrelated FILE, and any orphan still waiting for that FRN would
        // get wrongly attached to it.
        if (!index.IsDirectory(newRow))
            return;

        List<int>? waiting = null;
        foreach (var kv in index.OrphanParentFrns)
            if (kv.Value == frn)
                (waiting ??= new List<int>()).Add(kv.Key);
        if (waiting == null)
            return;

        foreach (var orphanRow in waiting)
        {
            index.ParentIndexes[orphanRow] = newRow;
            index.OrphanParentFrns.TryRemove(orphanRow, out _);
            if (!index.ParentToChildren.TryGetValue(newRow, out var children))
            {
                children = new List<int>();
                index.ParentToChildren[newRow] = children;
            }
            children.Add(orphanRow);
        }
    }

    internal static void TrimStorage(this RuntimeIndex index)
    {
        index.Ids.TrimExcess();
        index.ParentIndexes.TrimExcess();
        index.NameIds.TrimExcess();
        index.Flags.TrimExcess();
        index.CharMasks.TrimExcess();
        index.Sizes.TrimExcess();
        index.CreationTimes.TrimExcess();
        index.LastWriteTimes.TrimExcess();
        index.LastAccessTimes.TrimExcess();
    }

    internal static void AddColumns(this RuntimeIndex index, UInt128 id, string name, FileRecordFlags flags,
        long size = 0, uint creationTimeUtc = 0, uint lastWriteTimeUtc = 0, uint lastAccessTimeUtc = 0)
    {
        index.Ids.Add(id);
        index.NameIds.Add(index.Names.GetId(name));
        index.Flags.Add((ushort)flags);
        index.CharMasks.Add(FzfAlgorithm.GetCharMask(name));
        index.Sizes.Add(size);
        index.CreationTimes.Add(creationTimeUtc);
        index.LastWriteTimes.Add(lastWriteTimeUtc);
        index.LastAccessTimes.Add(lastAccessTimeUtc);
    }

    internal static string[]? GenerateAliases(this RuntimeIndex index, string name, out byte[] providerIds)
    {
        providerIds = Array.Empty<byte>();
        if (string.IsNullOrEmpty(name) || !AliasProviderRegistry.HasNonAscii(name))
            return null;

        List<string>? list = null;
        List<byte>? idList = null;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (provider.CanHandle(name))
                {
                    var provId = AliasProviderRegistry.GetProviderId(provider);
                    foreach (var alias in provider.GetAliases(name))
                    {
                        if (string.IsNullOrWhiteSpace(alias))
                            continue;

                        list ??= new List<string>();
                        idList ??= new List<byte>();
                        list.Add(alias.ToLowerInvariant());
                        idList.Add(provId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[RuntimeIndex] Error generating alias: {ex.Message}", LogLevel.Error);
            }
        }

        if (list != null && idList != null)
        {
            providerIds = idList.ToArray();
            return list.ToArray();
        }
        return null;
    }
}
