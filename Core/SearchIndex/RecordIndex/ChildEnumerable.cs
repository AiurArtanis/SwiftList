namespace SwiftList.Core.SearchIndex.RecordIndex;

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
        private int _listIndex;

        internal Enumerator(RuntimeIndex index, int parentIndex)
        {
            _index = index;
            _parentIndex = parentIndex;
            _currentIndex = -1;
            _listIndex = -1;
        }

        public int Current => _currentIndex;

        public bool MoveNext()
        {
            if (_parentIndex < 0)
                return false;

            if (!_index.TryGetChildren(_parentIndex, out var children) || children == null)
                return false;

            _listIndex++;
            while (_listIndex < children.Count)
            {
                var idx = children[_listIndex];
                if (!_index.IsDeleted(idx))
                {
                    _currentIndex = idx;
                    return true;
                }
                _listIndex++;
            }

            return false;
        }
    }
}
