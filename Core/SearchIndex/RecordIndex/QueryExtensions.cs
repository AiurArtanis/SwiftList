namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class QueryExtensions
{
    public static UInt128 GetId(this RuntimeIndex index, int i) => index.Ids[i];

    public static UInt128 GetParentId(this RuntimeIndex index, int i)
    {
        var parentIndex = index.ParentIndexes[i];
        if ((uint)parentIndex < (uint)index.Ids.Count)
            return index.Ids[parentIndex];
        // Orphan (parent wasn't in the index when linked): return the true parent FRN we stashed so the
        // record saves losslessly instead of collapsing to a self-parent. A genuine root falls back to self.
        return index.OrphanParentFrns.TryGetValue(i, out var parentFrn) ? parentFrn : index.Ids[i];
    }

    public static string GetName(this RuntimeIndex index, int i) => index.Names.GetValue(index.NameIds[i]);

    public static FileRecordFlags GetFlags(this RuntimeIndex index, int i) => (FileRecordFlags)index.Flags[i];

    public static bool IsDirectory(this RuntimeIndex index, int i) => (((FileRecordFlags)index.Flags[i]) & FileRecordFlags.Directory) != 0;

    public static bool IsDeleted(this RuntimeIndex index, int i) => (((FileRecordFlags)index.Flags[i]) & FileRecordFlags.Deleted) != 0;

    public static bool HasAlias(this RuntimeIndex index, int i)
    {
        if (i >= index.LoadedCount)
        {
            return index.DeltaNameAliases.TryGetValue(i, out var deltaAliases) && deltaAliases != null && deltaAliases.Length > 0;
        }
        return index.HasAlias.Get(i);
    }

    public static FileRecord GetRecord(this RuntimeIndex index, int i) => new FileRecord(index.Ids[i], index.GetParentId(i), index.GetName(i), (FileRecordFlags)index.Flags[i]);

    public static bool TryGetAliases(this RuntimeIndex index, int i, out string[] aliases) => index.TryGetAliases(i, out aliases, out _);

    public static bool TryGetAliases(this RuntimeIndex index, int i, out string[] aliases, out byte[] providerIds)
    {
        if (i >= index.LoadedCount)
        {
            if (index.DeltaNameAliases.TryGetValue(i, out var deltaAliases))
            {
                if (deltaAliases != null && deltaAliases.Length > 0)
                {
                    aliases = deltaAliases;
                    providerIds = index.DeltaAliasProviderIds.TryGetValue(i, out var dp) ? dp : Array.Empty<byte>();
                    return true;
                }
            }
            aliases = Array.Empty<string>();
            providerIds = Array.Empty<byte>();
            return false;
        }

        if (!index.HasAlias.Get(i))
        {
            aliases = Array.Empty<string>();
            providerIds = Array.Empty<byte>();
            return false;
        }

        if (index.DeltaNameAliases.TryGetValue(i, out var deltaAliasesOverridden))
        {
            if (deltaAliasesOverridden != null && deltaAliasesOverridden.Length > 0)
            {
                aliases = deltaAliasesOverridden;
                providerIds = index.DeltaAliasProviderIds.TryGetValue(i, out var dp) ? dp : Array.Empty<byte>();
                return true;
            }
            aliases = Array.Empty<string>();
            providerIds = Array.Empty<byte>();
            return false;
        }

        var pos = Array.BinarySearch(index.AliasIndices, i);
        if (pos >= 0)
        {
            aliases = index.AliasValues[pos];
            providerIds = index.AliasProviderIds[pos];
            return true;
        }

        aliases = Array.Empty<string>();
        providerIds = Array.Empty<byte>();
        return false;
    }

    internal static int ResolveParentIndex(this RuntimeIndex index, UInt128 id, UInt128 parentId) => parentId != id && index.TryGetIndexById(parentId, out var parentIndex) ? parentIndex : -1;

    public static bool TryGetIndexById(this RuntimeIndex index, UInt128 id, out int idx)
    {
        if (index.DeltaIdToIndex.TryGetValue(id, out idx))
        {
            if (!index.IsDeleted(idx))
                return true;

            idx = -1;
            return false;
        }

        if (index.LoadedCount > 0)
        {
            var low = 0;
            var high = index.LoadedCount - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var midId = index.Ids[mid];
                if (midId == id)
                {
                    if (!index.IsDeleted(mid))
                    {
                        idx = mid;
                        return true;
                    }
                    break;
                }
                if (midId < id)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
        }

        idx = -1;
        return false;
    }

    internal static void GetNameCandidateStorage(this RuntimeIndex index, string pattern, out int[]? bucket, out List<int>? delta)
    {
        bucket = null;
        delta = null;
        if (pattern.Length == 0)
            return;

        var bestKey = '\0';
        var minCount = int.MaxValue;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = char.ToLowerInvariant(pattern[i]);
            if (c == ' ' || c == '/' || c == '\\')
                continue;

            var count = 0;
            if (index.NameCharBuckets.TryGetValue(c, out var b))
            {
                count += b.Length;
            }
            if (index.NameCharDelta.TryGetValue(c, out var d))
            {
                count += d.Count;
            }

            if (count < minCount)
            {
                minCount = count;
                bestKey = c;
            }
        }

        if (bestKey != '\0')
        {
            index.NameCharBuckets.TryGetValue(bestKey, out bucket);
            index.NameCharDelta.TryGetValue(bestKey, out delta);
        }
        else
        {
            var key = char.ToLowerInvariant(pattern[0]);
            index.NameCharBuckets.TryGetValue(key, out bucket);
            index.NameCharDelta.TryGetValue(key, out delta);
        }
    }

}
