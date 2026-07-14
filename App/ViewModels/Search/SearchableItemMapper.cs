using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.App.Services;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

public static class SearchableItemMapper
{
    public static void Preload() => SearchableItemCache.Preload();

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items (see AddSearchableItemResults' cache-miss "continue" below) -- there is
    // no synchronous "wait for everything" alternative without blocking the UI. Instead, a live search
    // re-runs itself once more providers become available, so results stream in rather than staying
    // incomplete for the rest of the session. Raised on a background thread; subscribers must marshal
    // back to the UI thread themselves.
    public static event Action? ProviderLoaded
    {
        add => SearchableItemCache.ProviderLoaded += value;
        remove => SearchableItemCache.ProviderLoaded -= value;
    }

    private static string _lastFileFiltersSignature = string.Empty;
    private static string _lastCustomFoldersSignature = string.Empty;

    // Returns candidates (with their ranking weight) instead of appending directly to a results list --
    // the caller (SearchResultMapper.BuildQuickResults) merges these into one globally weight-sorted
    // list alongside favorites/history-matched files/file-search results, rather than always showing
    // every searchable item ahead of every file result regardless of which actually matched better.
    public static List<(AppSearchResult Result, double Weight)> CollectSearchableItemResults(string query, bool isInlineWindow)
    {
        var candidates = new List<(AppSearchResult Result, double Weight)>();
        if (isInlineWindow) return candidates;

        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q)) return candidates;

        // Perform efficient config signature check to auto-reload settings when user applies plugin config modifications
        var currentFilters = PluginSettingsService.GetSettingFunc?.Invoke("SwiftList.Plugins.FileFilters", "Filters", null);
        var signature = currentFilters != null
            ? System.Text.Json.JsonSerializer.Serialize(currentFilters)
            : string.Empty;

        if (signature != _lastFileFiltersSignature)
        {
            _lastFileFiltersSignature = signature;
            // Config changed: Evict FileFiltersSearchableItemProvider cache to force settings reload and directory scanning
            SearchableItemCache.Invalidate("FileFiltersSearchableItemProvider");
        }

        var currentCustomFolders = PluginSettingsService.GetSettingFunc?.Invoke("SwiftList.Plugins.CoreExtensions", "CustomFolders", null);
        var customFoldersSig = currentCustomFolders != null
            ? System.Text.Json.JsonSerializer.Serialize(currentCustomFolders)
            : string.Empty;
        if (customFoldersSig != _lastCustomFoldersSignature)
        {
            _lastCustomFoldersSignature = customFoldersSig;
            SearchableItemCache.Invalidate("StartMenuAppItemProvider");
        }

        // Parse prefix keyword (e.g. "tf avsa") -> keyword = "tf", subQuery = "avsa"
        var parts = q.Split(new[] { ' ' }, 2);
        var keyword = parts[0].Trim().ToLowerInvariant();
        var subQuery = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var isKeywordSearch = parts.Length > 1 || (parts.Length == 1 && q.EndsWith(" ", StringComparison.Ordinal));
        var targetFileFilterKind = $"FileFilter_{keyword}";

        // A keyword search only enters a file-filter scope when the first word actually matches a
        // registered filter keyword. Without this check, ANY space-containing query (e.g.
        // "visual studio") would be treated as a filter prefix and wrongly hide general items
        // such as Start Menu apps.
        var isKnownFilterKeyword = isKeywordSearch && IsRegisteredFilterKeyword(targetFileFilterKind);

        // Every matched entry -- across ALL providers, not just within one -- gets ranked by the same
        // percentage*consecutiveness weight the file search hot path uses (FuzzyMatcher.
        // ComputeMatchWeight, against the entry's own title -- same text TextHighlighter shows),
        // instead of a fixed match-kind bucket order capped PER PROVIDER. The old (and until-now
        // still-present) per-provider Take(8) meant results were really just provider-enumeration-
        // order chunks, each internally bucketed -- e.g. every Start Menu app ahead of every System
        // Settings item regardless of which actually matched better, since apps and settings are
        // different providers. Collecting across providers first and sorting/capping once fixes that.
        var matched = new List<(SearchableItemCache.CacheEntry Entry, double Weight, ISearchableItemProvider Provider, string ActiveQuery)>();

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);

            if (!SearchableItemCache.TryGetEntries(provider.Id, out var entries))
                continue;

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
                    // Case B: This is a normal searchable item (like Start Menu apps).
                    // Only hide it when the user is genuinely inside a file-filter scope, i.e. the
                    // first word is a registered filter keyword (e.g. "tf ..."). A plain multi-word
                    // query like "visual studio" is NOT a filter prefix and must keep showing apps.
                    if (isKnownFilterKeyword)
                    {
                        continue; // Skip: user is focused on a filter's directory, don't show general apps
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
                        // -- no query text to weight against, so these just keep their enumeration order.
                        matched.Add((entry, 1.0, provider, activeQuery));
                    }
                    continue;
                }

                if (activeQuery.Length < 2 && !isFileFilterItem) continue;

                // The standard match+weight contract (FuzzyMatcher.ComputeBestMatch): title first,
                // then each curated alias, via the same FzfPattern.Parse Core's real file search uses
                // -- a multi-word query like "gsh ypfq" correctly requires BOTH words to match
                // somewhere, unlike the old title.StartsWith/.Contains/MarkFuzzyMatch chain, which
                // treated the whole query (spaces included) as one literal/fuzzy string.
                var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch(activeQuery, entry.Item.Title, entry.Aliases);
                if (isMatch)
                    matched.Add((entry, weight, provider, activeQuery));
            }
        }

        // Generous safety cap only -- the real top-N selection happens after this merges with the
        // other candidate categories in BuildQuickResults.
        var matches = matched.OrderByDescending(m => m.Weight).Take(50);
        foreach (var (entry, weight, provider, activeQuery) in matches)
        {
            var item = entry.Item;
            System.Windows.Media.ImageSource? iconOverride = null;

            var isRealFile = false;
            var isRealDir = false;
            var isApplication = false;
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
            else if (rKind == "Application")
            {
                // Keep the app's real target path (a Start Menu .lnk, or a virtual shell:AppsFolder
                // token for packaged apps) instead of the generic "__SEARCHABLE_ITEM__:" placeholder,
                // so file actions (copy, locate in explorer, ...) have something to act on -- each
                // action's own CanExecute already handles a path that doesn't exist on disk.
                isApplication = true;
            }
            else if (isFileFilterItem)
            {
                // For FileFilter items, we infer they are files unless they have no extension, then fallback safely to Folder type
                var ext = Path.GetExtension(item.ActionArgument);
                if (!string.IsNullOrEmpty(ext)) isRealFile = true;
                else isRealDir = true;
            }

            if (entry.Icon != null)
            {
                // Frozen bitmap materialized once at load time (see EnsureLoaded); reused as-is with
                // no per-keystroke rebuild and no leaked GDI handle.
                iconOverride = entry.Icon;
            }
            else if ((isRealFile || isRealDir || isApplication) && !string.IsNullOrWhiteSpace(item.ActionArgument))
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
                    Logger.Log($"[SearchableItemMapper] Failed to create vector icon: {ex.Message}", LogLevel.Error);
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

            candidates.Add((new AppSearchResult
            {
                Name = item.Title,
                FullPath = (isRealFile || isRealDir || isApplication) ? item.ActionArgument : $"__SEARCHABLE_ITEM__:{provider.Name}:{item.Title}",
                // Applications show name-only: blank the subtitle so the path row collapses (an app's
                // FullPath is a virtual token anyway). Other item kinds keep their description.
                ParentDir = item.ResultKind == "Application" ? string.Empty : item.Description,
                IsDir = isRealDir,
                Drive = string.Empty,
                ResultKind = isRealFile ? "File" : (isRealDir ? "Directory" : (isApplication ? "Application" : "InstantResult")),
                SearchQuery = activeQuery ?? string.Empty,
                IconOverride = iconOverride,
                InstantResultActionType = item.ActionType ?? "Copy",
                InstantResultActionArgument = item.ActionArgument ?? string.Empty,
                InstantResultOnExecute = item.OnExecute,
                TabCompletion = item.TabCompletion,
                SourceProvider = provider
            }, weight));
        }

        return candidates;
    }

    private static bool IsRegisteredFilterKeyword(string targetFileFilterKind) => SearchableItemCache.IsRegisteredFilterKeyword(targetFileFilterKind);
}
