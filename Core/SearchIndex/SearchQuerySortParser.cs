namespace SwiftList.Core;

public enum QuerySortField { Size, Created, Modified, Accessed }

public readonly record struct QuerySortDirective(QuerySortField Field, bool Descending);

// Parses an optional trailing "<query> :[SCMA]" sort suffix off a raw search query -- e.g.
// "report :s" sorts by Size ascending, "report :s-,m" sorts by Size descending then Modified
// ascending as a tiebreaker. The suffix must be the query's last whitespace-separated token so it
// never gets misread out of the middle of an otherwise-unrelated search term.
public static class SearchQuerySortParser
{
    public static string Strip(string query, out IReadOnlyList<QuerySortDirective> sortDirectives)
    {
        sortDirectives = Array.Empty<QuerySortDirective>();

        var trimmed = query.TrimEnd();
        var lastSpaceIndex = trimmed.LastIndexOf(' ');
        var lastToken = lastSpaceIndex >= 0 ? trimmed[(lastSpaceIndex + 1)..] : trimmed;

        if (!TryParseDirectives(lastToken, out var directives))
            return query;

        sortDirectives = directives;
        return lastSpaceIndex >= 0 ? trimmed[..lastSpaceIndex] : string.Empty;
    }

    private static bool TryParseDirectives(string token, out List<QuerySortDirective> directives)
    {
        directives = new List<QuerySortDirective>();
        if (token.Length < 2 || token[0] != ':')
            return false;

        foreach (var part in token[1..].Split(','))
        {
            if (!TryParseSingle(part, out var directive))
            {
                directives.Clear();
                return false;
            }
            directives.Add(directive);
        }

        return directives.Count > 0;
    }

    private static bool TryParseSingle(string part, out QuerySortDirective directive)
    {
        directive = default;
        if (part.Length == 0)
            return false;

        var descending = false;
        var letterPart = part;
        if (letterPart[0] == '-')
        {
            descending = true;
            letterPart = letterPart[1..];
        }
        if (letterPart.Length > 0 && letterPart[^1] == '-')
        {
            descending = true;
            letterPart = letterPart[..^1];
        }
        if (letterPart.Length != 1)
            return false;

        QuerySortField field;
        switch (char.ToUpperInvariant(letterPart[0]))
        {
            case 'S': field = QuerySortField.Size; break;
            case 'C': field = QuerySortField.Created; break;
            case 'M': field = QuerySortField.Modified; break;
            case 'A': field = QuerySortField.Accessed; break;
            default: return false;
        }

        directive = new QuerySortDirective(field, descending);
        return true;
    }
}
