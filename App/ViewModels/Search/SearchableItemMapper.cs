using System.IO;
using System.Collections.Concurrent;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

public static class SearchableItemMapper
{
    private record CacheEntry(SearchableItem Item, List<string> Aliases);

    private static readonly ConcurrentDictionary<string, List<CacheEntry>> _cache = new();
    private static readonly ConcurrentDictionary<string, Task> _loadingTasks = new();
    private static readonly ConcurrentDictionary<string, bool> _subscribed = new();

    public static void Preload()
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            EnsureLoaded(provider);
        }
    }

    private static string _lastFileFiltersSignature = string.Empty;

    public static void AddSearchableItemResults(List<AppSearchResult> uiResults, string query, bool isInlineWindow)
    {
        if (isInlineWindow) return;

        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q)) return;

        // Perform efficient config signature check to auto-reload settings when user applies plugin config modifications
        var currentFilters = PluginSettingsService.GetSettingFunc?.Invoke("SwiftList.Plugins.FileFilters", "Filters", null);
        var signature = currentFilters != null
            ? System.Text.Json.JsonSerializer.Serialize(currentFilters)
            : string.Empty;

        if (signature != _lastFileFiltersSignature)
        {
            _lastFileFiltersSignature = signature;
            // Config changed: Evict FileFiltersSearchableItemProvider cache to force settings reload and directory scanning
            _cache.TryRemove("FileFiltersSearchableItemProvider", out _);
            _loadingTasks.TryRemove("FileFiltersSearchableItemProvider", out _);
        }

        // Parse prefix keyword (e.g. "tf avsa") -> keyword = "tf", subQuery = "avsa"
        var parts = q.Split(new[] { ' ' }, 2);
        var keyword = parts[0].Trim().ToLowerInvariant();
        var subQuery = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var isKeywordSearch = parts.Length > 1 || (parts.Length == 1 && q.EndsWith(" ", StringComparison.Ordinal));
        var targetFileFilterKind = $"FileFilter_{keyword}";

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            EnsureLoaded(provider);

            if (!_cache.TryGetValue(provider.Id, out var entries))
                continue;

            var prefixMatches = new List<CacheEntry>();
            var containsMatches = new List<CacheEntry>();
            var aliasMatches = new List<CacheEntry>();

            foreach (var entry in entries)
            {
                var rKind = entry.Item.ResultKind ?? string.Empty;
                var isFileFilterItem = rKind.StartsWith("FileFilter_", StringComparison.OrdinalIgnoreCase);

                if (isFileFilterItem)
                {
                    // Case A: This item belongs to a File Filter rule.
                    // We ONLY match it if user typed the corresponding keyword prefix (e.g. "tf ").
                    if (!isKeywordSearch || !string.Equals(rKind, targetFileFilterKind, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip: Does not match current prefix keyword search context
                    }
                }
                else
                {
                    // Case B: This is a normal searchable item (like Start Menu shortcuts, etc.).
                    // If user is currently running a keyword search (e.g. "tf avsa"), we FILTER OUT other general search items.
                    if (isKeywordSearch && targetFileFilterKind.StartsWith("FileFilter_", StringComparison.OrdinalIgnoreCase) &&
                        PluginManager.Instance.SearchableItemProviders.Any(p => p.GetType().FullName!.Contains("FileFilters")))
                    {
                        continue; // Skip: User is focused on this filter directory, don't show general apps
                    }
                }

                // If user is searching with prefix "tf avsa", match the file name against "avsa" (subQuery)
                var activeQuery = isFileFilterItem ? subQuery : q;

                // Do not run fuzzy matches if active query is empty (e.g. just typed "tf " but no search term yet)
                if (string.IsNullOrEmpty(activeQuery))
                {
                    if (isFileFilterItem)
                    {
                        // Return everything in the filter directory if user typed keyword with no query
                        prefixMatches.Add(entry);
                    }
                    continue;
                }

                if (activeQuery.Length < 2 && !isFileFilterItem) continue;

                var title = entry.Item.Title;
                if (title.StartsWith(activeQuery, StringComparison.OrdinalIgnoreCase))
                    prefixMatches.Add(entry);
                else if (title.Contains(activeQuery, StringComparison.OrdinalIgnoreCase))
                    containsMatches.Add(entry);
                else
                {
                    var highlights = new bool[title.Length];
                    Converters.FuzzyHighlightMatcher.MarkFuzzyMatch(title.ToLowerInvariant(), activeQuery.ToLowerInvariant(), highlights);
                    if (highlights.Any(h => h))
                    {
                        aliasMatches.Add(entry);
                    }
                    else if (entry.Aliases.Any(alias => alias.Contains(activeQuery, StringComparison.OrdinalIgnoreCase)))
                    {
                        aliasMatches.Add(entry);
                    }
                }
            }

            var matches = prefixMatches.Concat(containsMatches).Concat(aliasMatches).Take(8);
            foreach (var entry in matches)
            {
                var item = entry.Item;
                System.Windows.Media.ImageSource? iconOverride = null;

                var isRealFile = false;
                var isRealDir = false;
                var rKind = item.ResultKind ?? string.Empty;
                var isFileFilterItem = rKind.StartsWith("FileFilter_", StringComparison.OrdinalIgnoreCase);

                if (rKind == "File")
                {
                    isRealFile = true;
                }
                else if (rKind == "Directory")
                {
                    isRealDir = true;
                }
                else if (isFileFilterItem)
                {
                    // For FileFilter items, we infer they are files unless they have no extension, then fallback safely to Folder type
                    var ext = Path.GetExtension(item.ActionArgument);
                    if (!string.IsNullOrEmpty(ext)) isRealFile = true;
                    else isRealDir = true;
                }

                if (item.HBitmapIcon != IntPtr.Zero)
                {
                    try
                      {
                        iconOverride = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            item.HBitmapIcon, IntPtr.Zero,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                    catch { }
                }
                else if ((isRealFile || isRealDir) && !string.IsNullOrWhiteSpace(item.ActionArgument))
                {
                    // Fallback to ShellIconHelper so native high-fidelity shell thumbnails display correctly!
                    iconOverride = ShellIconHelper.GetIconForPath(item.ActionArgument, isRealDir);
                }
                else if (!string.IsNullOrWhiteSpace(item.IconData))
                {
                    try
                    {
                        var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                        iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.Log($"[SearchableItemMapper] Failed to create vector icon: {ex.Message}", Core.LogLevel.Error);
                    }
                }
                else
                {
                    try
                    {
                        iconOverride = ShellIconHelper.CreateVectorIcon("M7 2v11h3v9l7-12h-4l3-8z", "DefaultPluginIconColor");
                    }
                    catch { }
                }

                // If user is searching with prefix "tf avsa", pass active query for highlighter calculation
                var activeHighlighterQuery = isFileFilterItem ? subQuery : q;

                uiResults.Add(new AppSearchResult
                {
                    Name = item.Title,
                    FullPath = (isRealFile || isRealDir) ? item.ActionArgument : $"__SEARCHABLE_ITEM__:{provider.Name}:{item.Title}",
                    ParentDir = item.Description,
                    IsDir = isRealDir,
                    Drive = string.Empty,
                    ResultKind = isRealFile ? "File" : (isRealDir ? "Directory" : "InstantResult"),
                    Index = uiResults.Count,
                    SearchQuery = activeHighlighterQuery ?? string.Empty,
                    IconOverride = iconOverride,
                    InstantResultActionType = item.ActionType ?? "Copy",
                    InstantResultActionArgument = item.ActionArgument ?? string.Empty,
                    InstantResultOnExecute = item.OnExecute,
                    TabCompletion = item.TabCompletion,
                    SourceProvider = provider
                });
            }
        }
    }



    private static void EnsureLoaded(ISearchableItemProvider provider)
    {
        var id = provider.Id;
        if (_subscribed.TryAdd(id, true))
        {
            provider.ItemsChanged += () =>
            {
                _cache.TryRemove(id, out _);
                _loadingTasks.TryRemove(id, out _);
            };
        }

        if (_cache.ContainsKey(id)) return;

        _loadingTasks.GetOrAdd(id, _ => Task.Run(() =>
        {
            try
            {
                var rawItems = provider.GetSearchableItems() ?? Array.Empty<SearchableItem>();
                var entries = new List<CacheEntry>();
                foreach (var item in rawItems)
                {
                    if (item == null) continue;
                    var aliases = provider.EnableAlias
                        ? Core.AliasProviderRegistry.GetActiveProviders()
                            .Where(p => p.CanHandle(item.Title))
                            .SelectMany(p => p.GetAliases(item.Title))
                            .ToList()
                        : new List<string>();
                    entries.Add(new CacheEntry(item, aliases));
                }
                _cache[id] = entries;
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[SearchableItemMapper] Error loading from provider '{provider.Name}': {ex.Message}", Core.LogLevel.Error);
                _cache[id] = new List<CacheEntry>();
            }
        }));
    }
}
