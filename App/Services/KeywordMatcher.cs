using System;
using System.Collections.Generic;

namespace SwiftList.App.Services
{
    public readonly record struct KeywordMatch(string Keyword, string ArgumentText);

    public static class KeywordMatcher
    {
        public static KeywordMatch? TryMatchKeyword(string query, IReadOnlyList<string> keywords)
        {
            string trimmed = query.Trim();
            bool hasArgumentSeparator = trimmed.IndexOf(' ') >= 0;
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                string key = keyword.Trim();
                if (trimmed.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return new KeywordMatch(key, string.Empty);

                if (trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                    return new KeywordMatch(key, trimmed[(key.Length + 1)..].TrimStart());

                if (!hasArgumentSeparator && key.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                    return new KeywordMatch(key, string.Empty);
            }

            return null;
        }
    }
}
