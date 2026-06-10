using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SwiftList.Core.SearchIndex.Fzf
{
    internal sealed class FzfTopN
    {
        private readonly int _capacity;
        private readonly ulong[] _sortKeys;
        private readonly int[] _entryIndices;
        private readonly int[] _scores;
        private int _count = 0;
        private int _worstIndex = -1;

        public FzfTopN(int capacity)
        {
            _capacity = Math.Max(capacity, 1);
            _sortKeys = new ulong[_capacity];
            _entryIndices = new int[_capacity];
            _scores = new int[_capacity];
        }

        public int Count => _count;

        public void Add(FzfRank rank)
        {
            if (_count < _capacity)
            {
                _sortKeys[_count] = rank.SortKey;
                _entryIndices[_count] = rank.EntryIndex;
                _scores[_count] = rank.Score;
                _count++;
                if (_worstIndex < 0 || rank.SortKey > _sortKeys[_worstIndex])
                    _worstIndex = _count - 1;
                return;
            }

            if (rank.SortKey >= _sortKeys[_worstIndex])
                return;

            _sortKeys[_worstIndex] = rank.SortKey;
            _entryIndices[_worstIndex] = rank.EntryIndex;
            _scores[_worstIndex] = rank.Score;
            _worstIndex = FindWorstIndexSIMD(_sortKeys, _count);
        }

        public void AddRange(IEnumerable<FzfRank> ranks)
        {
            foreach (var rank in ranks)
                Add(rank);
        }

        public List<FzfRank> Finish(int limit)
        {
            var list = new List<FzfRank>(_count);
            for (int i = 0; i < _count; i++)
            {
                list.Add(new FzfRank(_entryIndices[i], _scores[i], _sortKeys[i]));
            }
            FzfRankRadixSorter.Sort(list);
            if (list.Count > limit)
                list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        private static int FindWorstIndexSIMD(ulong[] sortKeys, int count)
        {
            if (count < 8 || !Avx2.IsSupported)
            {
                int worst = 0;
                for (int i = 1; i < count; i++)
                {
                    if (sortKeys[i] > sortKeys[worst])
                        worst = i;
                }
                return worst;
            }

            var maxVals = Vector256.Create(sortKeys[0], sortKeys[1], sortKeys[2], sortKeys[3]);
            var maxIndices = Vector256.Create(0UL, 1UL, 2UL, 3UL);

            int limit = count - (count % 4);
            for (int i = 4; i < limit; i += 4)
            {
                var nextVals = Vector256.Create(sortKeys[i], sortKeys[i + 1], sortKeys[i + 2], sortKeys[i + 3]);
                var nextIndices = Vector256.Create((ulong)i, (ulong)(i + 1), (ulong)(i + 2), (ulong)(i + 3));

                var cmp = Vector256.GreaterThan(nextVals, maxVals);

                maxVals = Vector256.ConditionalSelect(cmp, nextVals, maxVals);
                maxIndices = Vector256.ConditionalSelect(cmp, nextIndices, maxIndices);
            }

            ulong bestVal = maxVals.GetElement(0);
            int bestIdx = (int)maxIndices.GetElement(0);

            for (int i = 1; i < 4; i++)
            {
                if (maxVals.GetElement(i) > bestVal)
                {
                    bestVal = maxVals.GetElement(i);
                    bestIdx = (int)maxIndices.GetElement(i);
                }
            }

            for (int i = limit; i < count; i++)
            {
                if (sortKeys[i] > bestVal)
                {
                    bestVal = sortKeys[i];
                    bestIdx = i;
                }
            }

            return bestIdx;
        }
    }
}
