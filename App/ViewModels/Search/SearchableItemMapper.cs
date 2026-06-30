using System.Collections.Concurrent;
using SwiftList.PluginSdk.Abstractions.Plugins;
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

    public static void AddSearchableItemResults(List<AppSearchResult> uiResults, string query, bool isInlineWindow)
    {
        if (isInlineWindow) return;

        var q = query?.Trim() ?? string.Empty;
        if (q.Length < 2) return;

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
                var title = entry.Item.Title;
                if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                    prefixMatches.Add(entry);
                else if (title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    containsMatches.Add(entry);
                else
                {
                    var highlights = new bool[title.Length];
                    Converters.FuzzyHighlightMatcher.MarkFuzzyMatch(title.ToLowerInvariant(), q.ToLowerInvariant(), highlights);
                    if (highlights.Any(h => h))
                    {
                        aliasMatches.Add(entry);
                    }
                    else if (entry.Aliases.Any(alias => alias.Contains(q, StringComparison.OrdinalIgnoreCase)))
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

                uiResults.Add(new AppSearchResult
                {
                    Name = item.Title,
                    FullPath = (item.ResultKind == "Application" || item.ResultKind == "File") ? item.ActionArgument : $"__SEARCHABLE_ITEM__:{provider.Name}:{item.Title}",
                    ParentDir = item.Description,
                    IsDir = false,
                    Drive = string.Empty,
                    ResultKind = item.ResultKind ?? "InstantResult",
                    Index = uiResults.Count,
                    SearchQuery = query ?? string.Empty,
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
