namespace SwiftList.Core.SearchIndex.RecordIndex
{
    public readonly struct ChildEnumerable
    {
        private readonly RuntimeIndex _index;
        private readonly int _parentIndex;

        internal ChildEnumerable(RuntimeIndex index, int parentIndex)
        {
            _index = index;
            _parentIndex = parentIndex;
        }

        public Enumerator GetEnumerator() => new(_index, _parentIndex);

        public struct Enumerator
        {
            private readonly RuntimeIndex _index;
            private readonly int _parentIndex;
            private int _currentIndex;

            internal Enumerator(RuntimeIndex index, int parentIndex)
            {
                _index = index;
                _parentIndex = parentIndex;
                _currentIndex = -1;
            }

            public int Current => _currentIndex;

            public bool MoveNext()
            {
                if (_parentIndex < 0)
                    return false;

                int count = _index.Count;
                for (int i = _currentIndex + 1; i < count; i++)
                {
                    if (!_index.IsDeleted(i) && _index.ParentIndexes[i] == _parentIndex)
                    {
                        _currentIndex = i;
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
