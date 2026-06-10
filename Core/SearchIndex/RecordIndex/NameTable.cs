namespace SwiftList.Core.SearchIndex.RecordIndex;

internal sealed class NameTable
{
    private readonly List<string> _values = new();
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    public void Clear()
    {
        _values.Clear();
        _ids.Clear();
    }

    public void ReleaseLookup()
    {
        _ids.Clear();
        _ids.TrimExcess();
        _values.TrimExcess();
    }

    public int GetId(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        if (_values.Count == 0)
            Add(string.Empty);

        if (_ids.TryGetValue(value, out var id))
            return id;

        return Add(value);
    }

    public string GetValue(int id) => (uint)id < (uint)_values.Count ? _values[id] : string.Empty;

    public char GetFirstChar(int id)
    {
        var value = GetValue(id);
        return value.Length == 0 ? '\0' : char.ToLowerInvariant(value[0]);
    }

    private int Add(string value)
    {
        var id = _values.Count;
        _values.Add(value);
        _ids[value] = id;
        return id;
    }
}
