using System;
using System.Collections.Generic;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal static class FzfRankRadixSorter
    {
        public static void Sort(List<FzfRank> ranks)
        {
            int count = ranks.Count;
            if (count < 128)
            {
                ranks.Sort(FzfResultRank.Compare);
                return;
            }

            var source = ranks.ToArray();
            var scratch = new FzfRank[count];
            var from = source;
            var to = scratch;
            int passes = 0;
            Span<int> buckets = stackalloc int[256];
            Span<int> offsets = stackalloc int[256];

            for (int pass = 0; pass < 8; pass++)
            {
                int shift = pass * 8;
                buckets.Clear();
                for (int i = 0; i < count; i++)
                    buckets[(int)((from[i].SortKey >> shift) & 0xFF)]++;

                int firstBucket = (int)((from[0].SortKey >> shift) & 0xFF);
                if (buckets[firstBucket] == count)
                    continue;

                offsets.Clear();
                for (int i = 1; i < 256; i++)
                    offsets[i] = offsets[i - 1] + buckets[i - 1];

                for (int i = 0; i < count; i++)
                {
                    int bucket = (int)((from[i].SortKey >> shift) & 0xFF);
                    to[offsets[bucket]++] = from[i];
                }

                (from, to) = (to, from);
                passes++;
            }

            if ((passes & 1) == 0)
                scratch = source;

            ranks.Clear();
            ranks.AddRange(from);
            SortEqualKeyRuns(ranks);
        }

        private static void SortEqualKeyRuns(List<FzfRank> ranks)
        {
            int start = 0;
            while (start < ranks.Count)
            {
                int end = start + 1;
                ulong key = ranks[start].SortKey;
                while (end < ranks.Count && ranks[end].SortKey == key)
                    end++;

                if (end - start > 1)
                    ranks.Sort(start, end - start, EntryIndexComparer.Instance);

                start = end;
            }
        }

        private sealed class EntryIndexComparer : IComparer<FzfRank>
        {
            public static readonly EntryIndexComparer Instance = new();

            public int Compare(FzfRank left, FzfRank right)
            {
                return left.EntryIndex.CompareTo(right.EntryIndex);
            }
        }
    }
}
