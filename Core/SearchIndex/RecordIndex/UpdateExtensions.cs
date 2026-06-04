using System;
using System.Collections.Generic;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core.SearchIndex.RecordIndex
{
    public static class UpdateExtensions
    {
        public static void Upsert(this RuntimeIndex index, FileRecord record)
        {
            string name = record.Name;

            if (index.TryGetIndexById(record.Id, out int oldIndex))
            {
                int newParentIndex = index.ResolveParentIndex(record.Id, record.ParentId);
                bool oldIsDirectory = index.IsDirectory(oldIndex);

                index.ParentIndexes[oldIndex] = newParentIndex;
                index.NameIds[oldIndex] = index.Names.GetId(name);
                index.Flags[oldIndex] = (byte)record.Flags;
                var newAliases = index.GenerateAliases(name);
                if (newAliases != null && newAliases.Length > 0)
                {
                    index.DeltaNameAliases[oldIndex] = newAliases;
                    if (oldIndex < index.LoadedCount)
                    {
                        index.HasAlias.Set(oldIndex, true);
                    }
                    index.CharMasks[oldIndex] = ulong.MaxValue;
                }
                else
                {
                    index.DeltaNameAliases[oldIndex] = Array.Empty<string>();
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

            int idx = index.Count;
            index.AddColumns(
                record.Id,
                name,
                record.Flags);
            index.DeltaIdToIndex[record.Id] = idx;

            int parentIndex = index.ResolveParentIndex(record.Id, record.ParentId);
            index.ParentIndexes.Add(parentIndex);

            if (record.IsDirectory)
                index.TotalDirs++;
            else
                index.TotalFiles++;

            var addedAliases = index.GenerateAliases(name);
            if (addedAliases != null && addedAliases.Length > 0)
            {
                index.DeltaNameAliases[idx] = addedAliases;
                index.CharMasks[idx] = ulong.MaxValue;
            }
            else if (record.IsDeleted)
            {
                index.CharMasks[idx] = 0;
            }

            index.AddNameCharDelta(name, idx);
            index.PathMemo.Clear();
        }

        public static void Remove(this RuntimeIndex index, ulong id)
        {
            if (!index.TryGetIndexById(id, out int idx))
                return;

            bool wasDirectory = index.IsDirectory(idx);
            index.Flags[idx] = (byte)(((FileRecordFlags)index.Flags[idx]) | FileRecordFlags.Deleted);
            index.CharMasks[idx] = 0;

            index.DeltaIdToIndex.Remove(id);
            index.DeltaNameAliases.Remove(idx);

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
        }

        internal static void TrimStorage(this RuntimeIndex index)
        {
            index.Ids.TrimExcess();
            index.ParentIndexes.TrimExcess();
            index.NameIds.TrimExcess();
            index.Flags.TrimExcess();
            index.CharMasks.TrimExcess();
        }

        internal static void AddColumns(this RuntimeIndex index, ulong id, string name, FileRecordFlags flags)
        {
            index.Ids.Add(id);
            index.NameIds.Add(index.Names.GetId(name));
            index.Flags.Add((byte)flags);
            index.CharMasks.Add(FzfAlgorithm.GetCharMask(name));
        }

        internal static string[]? GenerateAliases(this RuntimeIndex index, string name)
        {
            if (string.IsNullOrEmpty(name) || !AliasProviderRegistry.HasNonAscii(name))
                return null;

            List<string>? list = null;
            foreach (var provider in AliasProviderRegistry.GetActiveProviders())
            {
                try
                {
                    if (provider.CanHandle(name))
                    {
                        foreach (string alias in provider.GetAliases(name))
                        {
                            if (string.IsNullOrWhiteSpace(alias))
                                continue;

                            list ??= new List<string>();
                            list.Add(alias.ToLowerInvariant());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[RuntimeIndex] Error generating alias: {ex.Message}");
                }
            }

            return list?.ToArray();
        }
    }
}
