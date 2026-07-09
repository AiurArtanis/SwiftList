using SwiftList.PluginSdk.Abstractions;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

// Dispatches the raw tokens split out of a query's trailing ":a,b,c" suffix to whichever registered
// IQueryTokenProvider plugin claims each one, chaining the result through providers in token order.
// Only "ordinary" (File/Application) results are exposed to providers. A provider only ever reorders
// or shrinks that set -- it never grows it -- so the ordinary count after the whole chain runs is a
// reliable signal of whether any token actually filtered anything out. A token no provider claims
// isn't silently ignored either -- it reads as a typo'd/unsupported filter, and silently showing the
// un-narrowed file/app results would look like it worked when it didn't, so those are dropped -- but
// non-ordinary results (a calculator answer, system settings, ...) have nothing to do with a file
// filter and are kept regardless.
internal static class QueryTokenDispatcher
{
    public static async Task<List<AppSearchResult>> ApplyAsync(IReadOnlyList<AppSearchResult> results, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return results as List<AppSearchResult> ?? results.ToList();

        var ordinaryIndices = new List<int>();
        var ordinaryItems = new List<ISearchResult>();
        for (var i = 0; i < results.Count; i++)
        {
            if (IsOrdinaryResult(results[i]))
            {
                ordinaryIndices.Add(i);
                ordinaryItems.Add(results[i]);
            }
        }

        IReadOnlyList<ISearchResult> current = ordinaryItems;
        foreach (var token in tokens)
        {
            var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
            if (provider == null)
            {
                // Same reasoning as the "a token shrank the set" branch below: an unclaimed token also
                // invalidates the file-search portion of the query, so a "show N more" row pointing at
                // the pre-token count is just as stale/misleading here and gets dropped too -- only
                // non-ordinary rows unrelated to file filtering (a calculator answer, ...) survive.
                return results.Where(r => !IsOrdinaryResult(r) && r.ResultKind != "Action").ToList();
            }

            current = await provider.ApplyAsync(token, current);
        }

        // Nothing was actually filtered out (a pure sort, or a filter that happened to match
        // everything) -- non-ordinary rows (section headers, "show N more", instant results like a
        // calculator answer) still describe an accurate result set, so splice them back at their
        // original position instead of dropping them.
        if (current.Count == ordinaryItems.Count)
        {
            var ordinarySet = new HashSet<int>(ordinaryIndices);
            var spliced = new List<AppSearchResult>(results.Count);
            var next = 0;
            for (var i = 0; i < results.Count; i++)
            {
                spliced.Add(ordinarySet.Contains(i) ? (AppSearchResult)current[next++] : results[i]);
            }
            return spliced;
        }

        // A token actually shrank the ordinary set -- non-ordinary rows now describe a pre-filter
        // reality that's no longer true (e.g. a "show N more" pointing at a stale, larger count), so
        // drop them. Callers decide whether the resulting (possibly empty) list needs its own "no
        // results" placeholder (the quick/inline window renders one inline; the full search window
        // already has its own hint bound to an empty result list).
        return current.Cast<AppSearchResult>().ToList();
    }

    // Folders are ResultKind "File" too (IsDir just flags them) -- "Application" is the only other
    // kind that's a genuine file-backed result worth handing to a token provider.
    private static bool IsOrdinaryResult(AppSearchResult r) => r.ResultKind is "File" or "Application";
}
