using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SwiftList.Core.SearchIndex.Fzf;

namespace SwiftList.Core
{
    public class StartMenuAppIndex
    {
        private readonly object _lock = new();
        private List<AppEntry> _entries = new();

        public void Refresh()
        {
            var indexed = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            var entriesByName = new Dictionary<string, List<AppEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in StartMenuShortcutResolver.GetStartMenuRoots())
            {
                foreach (string path in StartMenuShortcutResolver.EnumerateFilesSafe(root))
                {
                    if (!StartMenuShortcutResolver.ShouldIndex(path) || indexed.ContainsKey(path))
                        continue;

                    string name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var entry = new AppEntry(name, path);
                    indexed[path] = entry;

                    if (!entriesByName.TryGetValue(name, out var entries))
                    {
                        entries = new List<AppEntry>();
                        entriesByName[name] = entries;
                    }

                    entries.Add(entry);
                }
            }

            var deduped = DeduplicateSameNameEntries(entriesByName);

            lock (_lock)
            {
                _entries = deduped
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            Logger.Log($"[StartMenuAppIndex] Indexed {_entries.Count} start menu applications.");
        }

        private static List<AppEntry> DeduplicateSameNameEntries(Dictionary<string, List<AppEntry>> entriesByName)
        {
            var deduped = new List<AppEntry>();

            foreach (var group in entriesByName.Values)
            {
                if (group.Count == 1)
                {
                    deduped.Add(group[0]);
                    continue;
                }

                var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in group)
                {
                    string key = StartMenuShortcutResolver.ResolveShortcutTarget(entry.Path) ?? entry.Path;
                    if (seenTargets.Add(key))
                        deduped.Add(entry);
                }
            }

            return deduped;
        }

        public List<SearchResult> Search(string query, int limit, CancellationToken token = default)
        {
            if (limit <= 0 || string.IsNullOrWhiteSpace(query))
                return new List<SearchResult>();

            AppEntry[] snapshot;
            lock (_lock)
            {
                snapshot = _entries.ToArray();
            }

            if (snapshot.Length == 0)
                return new List<SearchResult>();

            var pattern = FzfPattern.Parse(query);
            if (pattern.IsEmpty || pattern.TargetDrive != null)
                return new List<SearchResult>();

            int keep = Math.Max(limit * 4, 32);
            var matches = new FzfTopN(keep);
            var slab = new FzfSlab();
            for (int i = 0; i < snapshot.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = snapshot[i];
                if (!pattern.TryMatch(entry.Name, out var match, FzfScoringScheme.Default, slab))
                    continue;

                matches.Add(FzfResultRank.ForDefaultScheme(i, entry.Name, match));
            }

            var ranks = matches.Finish(limit);
            var results = new List<SearchResult>(Math.Min(limit, ranks.Count));
            foreach (var rank in ranks)
            {
                var entry = snapshot[rank.EntryIndex];
                results.Add(new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.Path,
                    IsDir = false,
                    Drive = string.Empty,
                    RankSortKey = rank.SortKey
                });
            }

            return results;
        }

        private sealed record AppEntry(string Name, string Path);
    }
}
