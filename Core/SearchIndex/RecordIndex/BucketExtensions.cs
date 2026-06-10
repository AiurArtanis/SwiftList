namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class BucketExtensions
{
    internal static void BuildNameCharBuckets(this RuntimeIndex index)
    {
        var builder = new Dictionary<char, List<int>>();
        var seenKeys = new HashSet<char>();
        for (var i = 0; i < index.Count; i++)
        {
            seenKeys.Clear();
            var primaryKey = index.Names.GetFirstChar(index.NameIds[i]);
            if (primaryKey != '\0')
            {
                seenKeys.Add(primaryKey);
            }

            var name = index.Names.GetValue(index.NameIds[i]);
            for (var charIdx = 0; charIdx < name.Length; charIdx++)
            {
                var c = name[charIdx];
                if (c > 127)
                {
                    seenKeys.Add(char.ToLowerInvariant(c));
                }
            }

            if (index.TryGetAliases(i, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    if (alias.Length > 0)
                    {
                        var startIdx = 0;
                        while (startIdx < alias.Length)
                        {
                            var key = alias[startIdx];
                            if (key != '|')
                            {
                                seenKeys.Add(key);
                            }
                            var nextPipe = alias.IndexOf('|', startIdx);
                            if (nextPipe < 0)
                                break;
                            startIdx = nextPipe + 1;
                        }
                    }
                }

                if (aliases.Length > 0 && aliases[0] != null)
                {
                    var initialsAlias = aliases[0];
                    for (var charIdx = 0; charIdx < initialsAlias.Length; charIdx++)
                    {
                        var key = initialsAlias[charIdx];
                        if (key != '|')
                        {
                            seenKeys.Add(key);
                        }
                    }
                }
            }

            foreach (var key in seenKeys)
            {
                if (!builder.TryGetValue(key, out var indexes))
                {
                    indexes = new List<int>();
                    builder[key] = indexes;
                }
                indexes.Add(i);
            }
        }

        index.SetNameCharBuckets(new Dictionary<char, int[]>(builder.Count));
        foreach (var kvp in builder)
            index.NameCharBuckets[kvp.Key] = kvp.Value.ToArray();
    }

    internal static void AddNameCharDelta(this RuntimeIndex index, string name, int idx)
    {
        if (name.Length == 0)
            return;

        var seenKeys = new HashSet<char>();
        seenKeys.Add(char.ToLowerInvariant(name[0]));

        for (var charIdx = 0; charIdx < name.Length; charIdx++)
        {
            var c = name[charIdx];
            if (c > 127)
            {
                seenKeys.Add(char.ToLowerInvariant(c));
            }
        }

        if (index.TryGetAliases(idx, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (alias.Length > 0)
                {
                    var startIdx = 0;
                    while (startIdx < alias.Length)
                    {
                        var key = alias[startIdx];
                        if (key != '|')
                        {
                            seenKeys.Add(key);
                        }
                        var nextPipe = alias.IndexOf('|', startIdx);
                        if (nextPipe < 0)
                            break;
                        startIdx = nextPipe + 1;
                    }
                }
            }

            if (aliases.Length > 0 && aliases[0] != null)
            {
                var initialsAlias = aliases[0];
                for (var charIdx = 0; charIdx < initialsAlias.Length; charIdx++)
                {
                    var key = initialsAlias[charIdx];
                    if (key != '|')
                    {
                        seenKeys.Add(key);
                    }
                }
            }
        }

        foreach (var key in seenKeys)
        {
            if (!index.NameCharDelta.TryGetValue(key, out var indexes))
            {
                indexes = new List<int>();
                index.NameCharDelta[key] = indexes;
            }

            if (indexes.Count > 0 && indexes[^1] == idx)
                continue;

            indexes.Add(idx);
        }
    }
}
