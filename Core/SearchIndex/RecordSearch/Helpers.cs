using System.Collections.Concurrent;
using System.Runtime;
using SwiftList.Core.SearchIndex.Fzf;
using SwiftList.Core.SearchIndex.RecordIndex;

namespace SwiftList.Core.SearchIndex.RecordSearch;

internal static class Helpers
{
    public const int ParallelNameSearchThreshold = 4096;
    public const int NameSearchChunkSize = 8192;
    public const int FastRerankMinimum = 1024;
    public const int FastInitialBudgetMilliseconds = 25;
    public const int BackgroundCompactionIntervalMilliseconds = 10_000;

    public static readonly ConcurrentDictionary<string, FzfPattern> PatternCache = new(StringComparer.Ordinal);
    private static long _lastBackgroundCompactionTicks;

    public static bool IsUnderDirectoryCached(
        this RuntimeIndex index,
        int entryIndex,
        int directoryRootIndex,
        Dictionary<int, bool> cache)
    {
        var tempStack = new List<int>();
        var current = entryIndex;
        var found = false;

        while (current >= 0)
        {
            if (cache.TryGetValue(current, out var cached))
            {
                found = cached;
                break;
            }

            if (current == directoryRootIndex)
            {
                found = true;
                break;
            }

            tempStack.Add(current);
            var parent = index.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        foreach (var idx in tempStack)
        {
            cache[idx] = found;
        }

        return found;
    }

    public static FzfPatternResult ToPatternResult(FzfMatchResult match) => new FzfPatternResult(match.Score, match.Start, match.End, match.End, match.IsMatch);

    public static List<SearchResult> Finish(this RuntimeIndex index, List<FzfRank> matches, int limit)
    {
        var results = new List<SearchResult>(Math.Min(limit, matches.Count));
        var seen = new HashSet<int>();
        foreach (var match in matches)
        {
            if (!seen.Add(match.EntryIndex))
                continue;

            results.Add(index.ToResult(match));
            if (results.Count >= limit)
                break;
        }

        return results;
    }

    public static FzfPattern GetPattern(string key, string query, bool parseText)
    {
        if (PatternCache.TryGetValue(key, out var cached))
            return cached;

        var pattern = parseText ? FzfPattern.ParseText(query) : FzfPattern.Parse(query);
        if (PatternCache.Count > 128)
            PatternCache.Clear();
        PatternCache[key] = pattern;
        return pattern;
    }

    public static SearchResult ToResult(this RuntimeIndex index, FzfRank rank)
    {
        var entryIndex = rank.EntryIndex;
        var flags = (FileRecordFlags)index.Flags[entryIndex];
        return new SearchResult
        {
            Name = index.GetName(entryIndex),
            Path = index.GetFullPath(entryIndex),
            IsDir = index.IsDirectory(entryIndex),
            Drive = index.SourceKey,
            Attributes = FileRecordFlagsHelper.ToAttributes(flags),
            RankSortKey = rank.SortKey,
            ModifiedUtc = index.GetLastWriteTimeUnixSeconds(entryIndex)
        };
    }

    public static string? NormalizeFilter(string? directoryFilter)
    {
        if (string.IsNullOrWhiteSpace(directoryFilter))
            return null;

        var value = directoryFilter.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
        return value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
    }

    public static UInt128? TryGetDirectoryRootId(this RuntimeIndex index, string? directoryFilterLower)
    {
        if (directoryFilterLower == null)
            return null;

        var sourceRootLower = index.SourceKey.ToLowerInvariant() + @":\";
        if (directoryFilterLower.Equals(sourceRootLower, StringComparison.Ordinal))
            return null;

        if (index.TryResolvePath(directoryFilterLower, out var id, out var childPrefixLower) &&
            childPrefixLower.Length == 0)
        {
            return id;
        }

        return null;
    }

    public static bool DirectoryFilterExcludesSource(this RuntimeIndex index, string? directoryFilterLower)
    {
        if (directoryFilterLower == null || directoryFilterLower.Length < 3)
            return false;

        if (char.IsLetter(directoryFilterLower[0]) &&
            directoryFilterLower[1] == Path.VolumeSeparatorChar &&
            directoryFilterLower[2] == Path.DirectorySeparatorChar)
        {
            return !directoryFilterLower[0].ToString().Equals(index.SourceKey, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static void RequestBackgroundCompaction()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastBackgroundCompactionTicks);
        if (now - last < BackgroundCompactionIntervalMilliseconds)
            return;

        if (Interlocked.CompareExchange(ref _lastBackgroundCompactionTicks, now, last) != last)
            return;

        _ = Task.Run(() =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        });
    }
}

internal sealed class MatcherWorkerContext
{
    private readonly int _keep;

    public MatcherWorkerContext(int keep)
    {
        _keep = keep;
        Slab = new FzfSlab();
        Matches = new FzfTopN(keep);
        Candidates = new List<int>();
    }

    public FzfSlab Slab { get; }
    public FzfTopN Matches { get; private set; }
    public List<int> Candidates { get; private set; }

    public void Reset()
    {
        Matches = new FzfTopN(_keep);
        Candidates.Clear();
    }

    public FzfTopN DetachMatches()
    {
        var matches = Matches;
        Matches = new FzfTopN(_keep);
        return matches;
    }

    public List<int> DetachCandidates()
    {
        var candidates = Candidates;
        Candidates = new List<int>();
        return candidates;
    }
}
