using System.IO;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

public static class SearchResultMapper
{
    // skipDisplayCap: token mode (SearchDispatchController.ComposeAndApplyAsync) still applies its own
    // final ~9-item cap AFTER filtering by the token, but needs the FULL ranked candidate set to filter
    // over first -- capping to the usual ~50 here, before a "::xxx"/directory-segment token ever runs,
    // silently drops the token's real matches whenever they don't also happen to be in the top ~50 by
    // plain filename weight (e.g. a common substring like "1080" already fills that cap with unrelated
    // files before the directory filter gets a chance to run at all).
    public static List<AppSearchResult> BuildQuickResults(List<SearchResult>? fileResults, string query, string? scope, string? contextDirectory, bool isInlineWindow, string? rawQuery = null, bool skipDisplayCap = false)
    {
        var uiResults = new List<AppSearchResult>();
        // Instant-result plugins get the untouched raw text (keyword + any " :xxx" token suffix) rather
        // than the stripped keyword everything else here uses -- a plugin like a calculator or unit
        // converter may care about the suffix itself, and it has no other way to see it since the token
        // is consumed before reaching here for every other purpose (file search, highlighting, ...).
        PluginSearchResultMapper.AddInstantResults(uiResults, rawQuery ?? query, query, isInlineWindow);

        if (fileResults != null && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var trimmed = query.Trim();
                var endsWithSeparator = trimmed.EndsWith("\\") || trimmed.EndsWith("/");
                if (trimmed.EndsWith(":\\") || trimmed.EndsWith(":/") || (endsWithSeparator && Directory.Exists(trimmed)))
                {
                    var normalizedQuery = SearchResultHelper.NormalizePath(trimmed);
                    fileResults.RemoveAll(x => string.Equals(SearchResultHelper.NormalizePath(x.Path), normalizedQuery, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
        }

        // If a directory scope is provided, keep only file/folder results that reside inside the scoped path.
        if (!string.IsNullOrEmpty(scope) && fileResults != null)
        {
            var normalizedScope = SearchResultHelper.NormalizePath(scope);
            fileResults = fileResults.FindAll(x =>
            {
                var normalizedPath = SearchResultHelper.NormalizePath(x.Path);
                return SearchResultHelper.IsPathInsideScope(normalizedPath, normalizedScope)
                    && !string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Plugin actions keep their own grouped-by-GroupName display (unlike everything below, these
        // are explicit keyword triggers the user deliberately typed, not fuzzy-guessed candidates, so
        // "how well did this match the query text" isn't a meaningful way to rank them against files/
        // apps/favorites) -- positioned right after instant results, before the weighted candidates.
        var hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow);

        var historySnapshot = SearchHistoryStore.Snapshot();

        // Favorites, history-matched files, searchable items (apps/settings), and remaining file
        // results all compete on ONE list now: history priority first (an explicit "you've opened
        // this before" signal -- items with no history sort after every item that has one), then
        // match-quality weight -- instead of every favorite always beating every app always beating
        // every file regardless of which one actually matched the query text better.
        var candidates = new List<RankedCandidate>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var favorites = UserSettings.Load().Favorites;
            for (var i = 0; i < favorites.Count; i++)
            {
                var fav = favorites[i];
                var (isMatch, weight) = FavoriteSearchHelper.ComputeMatch(fav, query);
                if (!isMatch)
                    continue;

                // A favorite is curated by the user regardless of whether it also has USAGE history --
                // that's a stronger signal than "matched the query text well" or "happens to be an
                // application", so it stays ahead of both.
                var lookupPath = fav.Path.Length > 3 && fav.Path[^1] == '\\' ? fav.Path.TrimEnd('\\') : fav.Path;
                var priority = historySnapshot.TryGetValue(lookupPath, out var hp) ? hp : int.MaxValue;
                candidates.Add(new RankedCandidate(
                    FavoriteSearchHelper.CreateFavoriteUiResult(fav, query, 0),
                    IsCurated: true,
                    priority,
                    weight,
                    SearchResultHelper.NormalizePath(fav.Path)));
            }
        }

        foreach (var (result, weight) in SearchableItemMapper.CollectSearchableItemResults(query, isInlineWindow))
        {
            candidates.Add(new RankedCandidate(result, IsCurated: false, int.MaxValue, weight, SearchResultHelper.NormalizePath(result.FullPath)));
        }

        if (fileResults != null)
        {
            foreach (var result in fileResults)
            {
                var lookupPath = result.Path.Length > 3 && result.Path[^1] == '\\' ? result.Path.TrimEnd('\\') : result.Path;
                var hasHistory = historySnapshot.TryGetValue(lookupPath, out var priority);
                candidates.Add(new RankedCandidate(
                    SearchResultHelper.CreateUiResult(result, query, 0, isApplication: false, scope),
                    IsCurated: hasHistory,
                    hasHistory ? priority : int.MaxValue,
                    FuzzyMatcher.ComputeMatchWeight(result.Name, query),
                    SearchResultHelper.NormalizePath(result.Path)));
            }
        }

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<AppSearchResult>();
        foreach (var candidate in candidates
                     // Favorites and history-matched files (an explicit "you use/opened this" signal)
                     // outrank everything else, applications included -- a coincidentally tighter text
                     // match on some unrelated app shouldn't bump a favorite or recently-used file.
                     .OrderByDescending(c => c.IsCurated)
                     .ThenBy(c => c.Priority)
                     // Within the same curated-ness tier, applications get their own priority over
                     // settings/files -- a launcher's primary use case is opening apps, and a
                     // coincidentally tighter text match on some unrelated file (e.g. a
                     // "visualstudio.py" build script fully containing "visual stu" contiguously)
                     // shouldn't outrank the actual "Visual Studio" application just because its own
                     // name has a space breaking up the match.
                     .ThenByDescending(c => c.Result.ResultKind == "Application")
                     .ThenByDescending(c => c.Weight)
                     .ThenBy(c => c.NormalizedPath.Length)
                     .ThenBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate.Result);
        }

        // Capped here (not deferred to the caller) because this display cap has to respect whatever
        // header/grouping layout the caller (or InlineListSearchHelper.MergeLocalMatches, downstream)
        // builds around these rows -- e.g. the inline window's "Current Folder"/"Global Search" split
        // needs its own files to stay adjacent to its own header. SearchDispatchController only takes
        // over capping/filtering once a query token is active, since token mode collapses that
        // grouping anyway (see its own composition logic). Same two-tier shape as before (show
        // everything under 10 total, else pad to ~8 then allow up to 50 with a "N more" marker) --
        // just applied to the now-unified candidate list instead of only file results.
        if (skipDisplayCap || uiResults.Count + ranked.Count < 10)
        {
            foreach (var result in ranked)
            {
                result.Index = uiResults.Count;
                uiResults.Add(result);
            }

            return uiResults;
        }

        var firstCount = Math.Min(ranked.Count, Math.Max(0, 8 - uiResults.Count));
        for (var i = 0; i < firstCount; i++)
        {
            ranked[i].Index = uiResults.Count;
            uiResults.Add(ranked[i]);
        }

        var hasMoreAtEnd = ranked.Count > 50;
        var endLimit = hasMoreAtEnd ? 50 : ranked.Count;

        for (var i = firstCount; i < endLimit; i++)
        {
            ranked[i].Index = uiResults.Count;
            uiResults.Add(ranked[i]);
        }

        if (hasMoreAtEnd)
        {
            SearchResultHelper.AddShowMoreResult(uiResults, query);
        }

        return uiResults;
    }

    private readonly record struct RankedCandidate(AppSearchResult Result, bool IsCurated, int Priority, double Weight, string NormalizedPath);

    public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
        => SearchResultHelper.CreateUiResult(item, query, index, isApplication, scope);

    public static AppSearchResult CreateNoResultsResult(string query)
        => SearchResultHelper.CreateNoResultsResult(query);

    public static string FormatSearchStatus(int appCount, int fileCount)
        => SearchResultHelper.FormatSearchStatus(appCount, fileCount);

    public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query)
        => SearchResultHelper.AddSectionHeader(uiResults, title, query);
}
