namespace SwiftList.Core;

// Splits an optional trailing "<query> :a,b,c" suffix off a raw search query into raw tokens --
// deliberately dumb: it has no idea what a token means (that's up to whichever IQueryTokenProvider
// plugin claims it). The suffix must be the query's last whitespace-separated token so it never
// gets misread out of the middle of an otherwise-unrelated search term.
public static class SearchQuerySortParser
{
    public static string Strip(string query, out IReadOnlyList<string> tokens)
    {
        tokens = Array.Empty<string>();

        var trimmed = query.TrimEnd();
        var lastSpaceIndex = trimmed.LastIndexOf(' ');
        var lastToken = lastSpaceIndex >= 0 ? trimmed[(lastSpaceIndex + 1)..] : trimmed;

        if (lastToken.Length < 2 || lastToken[0] != ':')
            return query;

        var parts = lastToken[1..].Split(',');
        if (parts.Any(p => p.Length == 0))
            return query;

        tokens = parts;
        return lastSpaceIndex >= 0 ? trimmed[..lastSpaceIndex] : string.Empty;
    }
}
