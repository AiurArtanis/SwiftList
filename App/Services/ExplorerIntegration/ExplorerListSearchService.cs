using System;
using System.Collections.Generic;
using System.IO;
using SwiftList.Core;

namespace SwiftList.App.Services
{
    public static class ExplorerListSearchService
    {
        private const int MaxCachedItems = 50_000;
        private static readonly object CacheGate = new();
        private static IntPtr _cachedHwnd;
        private static string _cachedPath = string.Empty;
        private static DateTime _cachedAt = DateTime.MinValue;
        private static List<ExplorerListItem> _cachedItems = new();

        public static List<AppSearchResult> Search(IntPtr explorerHwnd, string folderPath, string query, int limit)
        {
            if (explorerHwnd == IntPtr.Zero ||
                string.IsNullOrWhiteSpace(folderPath) ||
                string.IsNullOrWhiteSpace(query) ||
                limit <= 0)
            {
                return new List<AppSearchResult>();
            }

            var items = GetItems(explorerHwnd, folderPath);
            if (items.Count == 0)
                return new List<AppSearchResult>();

            var matches = new List<ExplorerListMatch>(Math.Min(items.Count, limit * 4));
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (TryMatch(item.Name, query, out int score, out int firstIndex, out int span))
                {
                    matches.Add(new ExplorerListMatch(item, score, firstIndex, span));
                }
                else
                {
                    // Fallback: try matching computed aliases
                    foreach (string alias in GenerateAliases(item.Name))
                    {
                        if (TryMatch(alias, query, out int aliasScore, out int aliasFirstIndex, out int aliasSpan))
                        {
                            matches.Add(new ExplorerListMatch(item, aliasScore - 100, aliasFirstIndex, aliasSpan));
                            break;
                        }
                    }
                }
            }

            matches.Sort(static (left, right) =>
            {
                int compare = right.Score.CompareTo(left.Score);
                if (compare != 0) return compare;

                compare = left.FirstIndex.CompareTo(right.FirstIndex);
                if (compare != 0) return compare;

                compare = left.Span.CompareTo(right.Span);
                if (compare != 0) return compare;

                compare = left.Item.Name.Length.CompareTo(right.Item.Name.Length);
                if (compare != 0) return compare;

                return string.Compare(left.Item.Name, right.Item.Name, StringComparison.OrdinalIgnoreCase);
            });

            int count = Math.Min(limit, matches.Count);
            var results = new List<AppSearchResult>(count);
            for (int i = 0; i < count; i++)
            {
                var item = matches[i].Item;
                results.Add(new AppSearchResult
                {
                    Name = item.Name,
                    FullPath = item.Path,
                    ParentDir = string.Empty,
                    ContextDirectory = item.IsDirectory ? item.Path : folderPath,
                    IsDir = item.IsDirectory,
                    Drive = Path.GetPathRoot(item.Path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar) ?? string.Empty,
                    ResultKind = "File",
                    Index = i,
                    SearchQuery = query
                });
            }

            return results;
        }

        public static void Invalidate(IntPtr explorerHwnd, string folderPath)
        {
            lock (CacheGate)
            {
                if (_cachedHwnd == explorerHwnd &&
                    string.Equals(_cachedPath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedItems = new List<ExplorerListItem>();
                    _cachedAt = DateTime.MinValue;
                }
            }
        }

        private static List<ExplorerListItem> GetItems(IntPtr explorerHwnd, string folderPath)
        {
            lock (CacheGate)
            {
                if (_cachedHwnd == explorerHwnd &&
                    string.Equals(_cachedPath, folderPath, StringComparison.OrdinalIgnoreCase) &&
                    (DateTime.Now - _cachedAt).TotalMilliseconds < 1500)
                {
                    return _cachedItems;
                }
            }

            var loaded = LoadItems(explorerHwnd, folderPath);
            lock (CacheGate)
            {
                _cachedHwnd = explorerHwnd;
                _cachedPath = folderPath;
                _cachedAt = DateTime.Now;
                _cachedItems = loaded;
                return _cachedItems;
            }
        }

        private static List<ExplorerListItem> LoadItems(IntPtr explorerHwnd, string folderPath)
        {
            var items = new List<ExplorerListItem>();
            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
                if (shellWindowsType == null)
                    return items;

                dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
                int count = shellWindows.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic window = shellWindows.Item(i);
                        object? hwndValue = window?.HWND;
                        if (hwndValue == null)
                            continue;

                        var windowHwnd = new IntPtr(Convert.ToInt64(hwndValue));
                        if (windowHwnd != explorerHwnd)
                            continue;

                        dynamic folderItems = window!.Document.Folder.Items();
                        int itemCount = folderItems.Count;
                        int cappedCount = Math.Min(itemCount, MaxCachedItems);
                        for (int itemIndex = 0; itemIndex < cappedCount; itemIndex++)
                        {
                            dynamic? folderItem = folderItems.Item(itemIndex);
                            if (folderItem == null)
                                continue;

                            string path = folderItem.Path;
                            if (string.IsNullOrWhiteSpace(path))
                                continue;

                            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            if (string.IsNullOrWhiteSpace(name))
                                name = folderItem.Name;
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            if (path.StartsWith("::", StringComparison.Ordinal) ||
                                path.Contains("::{", StringComparison.Ordinal) ||
                                path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            bool isFolder = Directory.Exists(path) || !File.Exists(path) && folderItem.IsFolder;
                            items.Add(new ExplorerListItem(name, path, isFolder));
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.Log($"[ExplorerListSearch] Failed to read Explorer window {i}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[ExplorerListSearch] Failed to load Explorer list: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }

            return items;
        }

        private static bool TryMatch(string text, string query, out int score, out int firstIndex, out int span)
        {
            score = 0;
            firstIndex = -1;
            span = int.MaxValue;

            string trimmedQuery = query.Trim();
            if (trimmedQuery.Length == 0)
                return false;

            int textIndex = 0;
            int lastIndex = -1;
            int contiguous = 0;
            int maxContiguous = 0;
            for (int queryIndex = 0; queryIndex < trimmedQuery.Length; queryIndex++)
            {
                char needle = char.ToUpperInvariant(trimmedQuery[queryIndex]);
                bool found = false;
                while (textIndex < text.Length)
                {
                    if (char.ToUpperInvariant(text[textIndex]) == needle)
                    {
                        if (firstIndex < 0)
                            firstIndex = textIndex;

                        contiguous = lastIndex + 1 == textIndex ? contiguous + 1 : 1;
                        maxContiguous = Math.Max(maxContiguous, contiguous);
                        lastIndex = textIndex;
                        textIndex++;
                        found = true;
                        break;
                    }

                    textIndex++;
                }

                if (!found)
                    return false;
            }

            span = lastIndex - firstIndex + 1;
            score = trimmedQuery.Length * 1000;
            if (text.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                score += 5000;
            else if (firstIndex == 0 || IsBoundary(text, firstIndex))
                score += 2500;

            score += maxContiguous * 200;
            score -= span * 10;
            score -= firstIndex;
            return true;
        }

        private static bool IsBoundary(string text, int index)
        {
            if (index <= 0)
                return true;

            char previous = text[index - 1];
            char current = text[index];
            return !char.IsLetterOrDigit(previous) ||
                   char.IsLower(previous) && char.IsUpper(current);
        }

        private static IEnumerable<string> GenerateAliases(string name)
        {
            if (string.IsNullOrEmpty(name) || !AliasProviderRegistry.HasNonAscii(name))
                yield break;

            foreach (var provider in AliasProviderRegistry.GetActiveProviders())
            {
                IEnumerable<string>? aliases = null;
                try
                {
                    if (provider.CanHandle(name))
                    {
                        aliases = provider.GetAliases(name);
                    }
                }
                catch
                {
                    // Ignore
                }

                if (aliases != null)
                {
                    foreach (string alias in aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(alias))
                            yield return alias;
                    }
                }
            }
        }

        private sealed record ExplorerListItem(string Name, string Path, bool IsDirectory);
        private sealed record ExplorerListMatch(ExplorerListItem Item, int Score, int FirstIndex, int Span);
    }
}
