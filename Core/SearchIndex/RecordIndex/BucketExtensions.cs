namespace SwiftList.Core.SearchIndex.RecordIndex;

public static class BucketExtensions
{
    internal static void BuildNameCharBuckets(this RuntimeIndex index)
    {
        var counts = new Dictionary<char, int>();
        var seenKeys = new HashSet<char>();

        // Pass 1: Count the exact occurrences of each character to preallocate arrays
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
                seenKeys.Add(char.ToLowerInvariant(name[charIdx]));
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
                counts[key] = counts.TryGetValue(key, out var cnt) ? cnt + 1 : 1;
            }
        }

        // Allocate exact-size arrays
        var buckets = new Dictionary<char, int[]>(counts.Count);
        var pointers = new Dictionary<char, int>(counts.Count);
        foreach (var kvp in counts)
        {
            buckets[kvp.Key] = new int[kvp.Value];
            pointers[kvp.Key] = 0;
        }

        // Pass 2: Populate the arrays directly without any resizing
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
                seenKeys.Add(char.ToLowerInvariant(name[charIdx]));
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
                var arr = buckets[key];
                var ptr = pointers[key];
                arr[ptr] = i;
                pointers[key] = ptr + 1;
            }
        }

        index.SetNameCharBuckets(buckets);
    }

    internal static void AddNameCharDelta(this RuntimeIndex index, string name, int idx)
    {
        if (name.Length == 0)
            return;

        var seenKeys = new HashSet<char>();
        for (var charIdx = 0; charIdx < name.Length; charIdx++)
        {
            var c = name[charIdx];
            seenKeys.Add(char.ToLowerInvariant(c));
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
