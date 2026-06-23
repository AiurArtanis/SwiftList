namespace SwiftList.Core;

public static class SearchHistoryStore
{
    private const int MaxEntries = 2000;
    private static readonly object Gate = new();
    private static Dictionary<string, int>? _priorityCache;
    private static List<string>? _entriesCache;

    public static string HistoryPath => Path.Combine(Logger.UserDataDir, "search-history.txt");

    public static void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("__", StringComparison.Ordinal) || !UserSettings.Load().EnableHistory)
            return;

        var normalized = NormalizePath(path);
        if (!File.Exists(normalized) && !Directory.Exists(normalized))
            return;

        lock (Gate)
        {
            EnsureCacheNoLock();
            if (_entriesCache == null)
                _entriesCache = new List<string>();

            _entriesCache.RemoveAll(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            _entriesCache.Insert(0, normalized);

            if (_entriesCache.Count > MaxEntries)
                _entriesCache.RemoveRange(MaxEntries, _entriesCache.Count - MaxEntries);

            try
            {
                Directory.CreateDirectory(Logger.UserDataDir);
                File.WriteAllLines(HistoryPath, _entriesCache);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchHistoryStore] Failed to write history: {ex.Message}", LogLevel.Error);
            }

            _priorityCache = BuildPriorityCache(_entriesCache);
        }
    }

    public static int GetPriority(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return int.MaxValue;

        lock (Gate)
        {
            EnsureCacheNoLock();
            var normalized = NormalizePath(path);
            return _priorityCache != null && _priorityCache.TryGetValue(normalized, out var priority)
                ? priority
                : int.MaxValue;
        }
    }

    public static IReadOnlyList<string> GetEntries()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _entriesCache != null ? _entriesCache.ToList() : new List<string>();
        }
    }

    public static void SaveEntries(IEnumerable<string> entries)
    {
        lock (Gate)
        {
            _entriesCache = entries.Select(NormalizePath)
                .Where(x => File.Exists(x) || Directory.Exists(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();

            try
            {
                Directory.CreateDirectory(Logger.UserDataDir);
                File.WriteAllLines(HistoryPath, _entriesCache);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchHistoryStore] Failed to write history: {ex.Message}", LogLevel.Error);
            }

            _priorityCache = BuildPriorityCache(_entriesCache);
        }
    }

    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (Gate)
        {
            EnsureCacheNoLock();
            return _priorityCache != null
                ? new Dictionary<string, int>(_priorityCache, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void EnsureCacheNoLock()
    {
        if (_priorityCache != null)
            return;

        _entriesCache = ReadEntriesNoLock();
        _priorityCache = BuildPriorityCache(_entriesCache);
    }

    private static List<string> ReadEntriesNoLock()
    {
        if (!File.Exists(HistoryPath))
            return new List<string>();

        try
        {
            return File.ReadLines(HistoryPath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchHistoryStore] Failed to read history: {ex.Message}", LogLevel.Error);
            return new List<string>();
        }
    }

    private static Dictionary<string, int> BuildPriorityCache(IReadOnlyList<string> entries)
    {
        var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            if (!priorities.ContainsKey(entries[i]))
                priorities[entries[i]] = i;
        }

        return priorities;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Trim('"')
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar);
    }
}
