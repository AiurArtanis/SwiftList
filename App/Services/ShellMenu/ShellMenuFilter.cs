using SwiftList.Core;

namespace SwiftList.App.Services;

public static class ShellMenuFilter
{
    public static List<ActionMenuItem> Apply(List<ActionMenuItem> rawItems, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return rawItems;
        }

        // Check if there are active Pinyin or other transliteration providers
        var activeProviders = AliasProviderRegistry.GetActiveProviders().ToList();

        var filtered = rawItems.Where(item => 
        {
            if (item.IsSectionHeader || item.IsSeparator)
            {
                return true;
            }

            if (string.IsNullOrEmpty(item.Text))
            {
                return false;
            }

            // Standardize spaces to support multi-keyword matching like FZF
            var queryKeywords = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (queryKeywords.Length == 0)
            {
                return true;
            }

            // Check if every keyword matches either the item text or its aliases (via fuzzy matching helper logic)
            foreach (var keyword in queryKeywords)
            {
                var isKeywordMatch = false;

                // 1. Direct Fuzzy Match (simplified sequence match)
                if (IsFuzzyMatch(item.Text, keyword))
                {
                    isKeywordMatch = true;
                }
                else
                {
                    // 2. Alias Match (enforce a cleaner substring match on concatenated pinyin aliases to prevent subsequence leaking)
                    foreach (var provider in activeProviders)
                    {
                        try
                        {
                            if (provider.CanHandle(item.Text))
                            {
                                foreach (var alias in provider.GetAliases(item.Text))
                                {
                                    if (!string.IsNullOrEmpty(alias) && alias.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isKeywordMatch = true;
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore provider exceptions
                        }

                        if (isKeywordMatch)
                        {
                            break;
                        }
                    }
                }

                if (!isKeywordMatch)
                {
                    return false;
                }
            }

            return true;
        }).ToList();

        // Clean up consecutive separators or headers without items
        var cleanItems = new List<ActionMenuItem>();
        for (var i = 0; i < filtered.Count; i++)
        {
            var current = filtered[i];
            if (current.IsSeparator)
            {
                if (cleanItems.Count > 0 && !cleanItems[^1].IsSeparator && !cleanItems[^1].IsSectionHeader)
                {
                    cleanItems.Add(current);
                }
            }
            else if (current.IsSectionHeader)
            {
                var hasItems = false;
                for (var j = i + 1; j < filtered.Count; j++)
                {
                    if (filtered[j].IsSectionHeader) break;
                    if (!filtered[j].IsSeparator && !filtered[j].IsDisabled && !filtered[j].IsSectionHeader)
                    {
                        hasItems = true;
                        break;
                    }
                }
                if (hasItems) cleanItems.Add(current);
            }
            else
            {
                cleanItems.Add(current);
            }
        }
        
        while (cleanItems.Count > 0 && (cleanItems[^1].IsSeparator || cleanItems[^1].IsSectionHeader))
        {
            cleanItems.RemoveAt(cleanItems.Count - 1);
        }

        while (cleanItems.Count > 0 && (cleanItems[0].IsSeparator || cleanItems[0].IsSectionHeader))
        {
            cleanItems.RemoveAt(0);
        }

        // Return empty list if no actual items were matched
        if (cleanItems.Count == 0 || cleanItems.All(x => x.IsSeparator || x.IsSectionHeader))
        {
            return new List<ActionMenuItem>();
        }

        return cleanItems;
    }

    private static bool IsFuzzyMatch(string target, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(target)) return false;

        var targetIdx = 0;
        var queryIdx = 0;

        while (targetIdx < target.Length && queryIdx < query.Length)
        {
            var targetChar = char.ToLowerInvariant(target[targetIdx]);
            var queryChar = char.ToLowerInvariant(query[queryIdx]);

            if (targetChar == queryChar)
            {
                queryIdx++;
            }
            targetIdx++;
        }

        return queryIdx == query.Length;
    }
}
