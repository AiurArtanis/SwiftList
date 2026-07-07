using SwiftList.PluginSdk.Abstractions;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

// Dispatches the raw tokens split out of a query's trailing ":a,b,c" suffix to whichever registered
// IQueryTokenProvider plugin claims each one, chaining the result through providers in token order.
// Only "ordinary" (File/Application) results are exposed to providers -- section headers, instant
// results, and other synthetic rows are spliced back untouched at their original position. A token
// no provider claims is simply skipped; the rest of the suffix still applies.
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
        if (ordinaryItems.Count > 0)
        {
            foreach (var token in tokens)
            {
                var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
                if (provider == null)
                    continue;

                current = await provider.ApplyAsync(token, current);
            }
        }

        // Sort-only tokens preserve count (current.Count == ordinaryIndices.Count); a filter token
        // can only shrink it. Filling ordinary slots front-to-back with whatever remains in `current`
        // reproduces the (possibly reordered) items in their new order and simply leaves trailing
        // slots unfilled -- i.e. dropped -- once `current` runs out, compacting the list correctly.
        var ordinarySet = new HashSet<int>(ordinaryIndices);
        var output = new List<AppSearchResult>(results.Count);
        var next = 0;
        for (var i = 0; i < results.Count; i++)
        {
            if (!ordinarySet.Contains(i))
            {
                output.Add(results[i]);
                continue;
            }
            if (next < current.Count)
                output.Add((AppSearchResult)current[next++]);
        }
        return output;
    }

    // Folders are ResultKind "File" too (IsDir just flags them) -- "Application" is the only other
    // kind that's a genuine file-backed result worth handing to a token provider.
    private static bool IsOrdinaryResult(AppSearchResult r) => r.ResultKind is "File" or "Application";
}
