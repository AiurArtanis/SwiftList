using System.Text.Json;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.WebSearch;

public class WebSearchInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WebSearch_ProviderName");

    public string Description => TranslationService.Get("WebSearch_ProviderDesc");

    public class SearchSourceItem
    {
        public string Name { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SuggestUrl { get; set; } = string.Empty;
    }

    private const int MaxSuggestions = 5;
    private static readonly TimeSpan SuggestionDebounce = TimeSpan.FromMilliseconds(200);

    private static readonly HttpClient SuggestionHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly HashSet<string> PendingSuggestionRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> SuggestionCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private static string? _latestSuggestionRequestKey;

    private const string PluginId = "SwiftList.Plugins.WebSearch";

    static WebSearchInstantProvider()
    {
        try
        {
            // Some suggestion endpoints (e.g. Wikipedia's) reject requests with no User-Agent header.
            SuggestionHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");
        }
        catch { }

        // Invalidate the cached sources as soon as the host reports this plugin's settings were
        // saved, so config changes apply to the very next keystroke instead of requiring a restart.
        PluginSettingsService.SettingChanged += (pluginId, key) =>
        {
            if (string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase))
            {
                _cachedSources = null;
            }
        };
    }

    private static List<SearchSourceItem>? _cachedSources;

    private static List<SearchSourceItem> GetDefaultSearchSources() => new List<SearchSourceItem>
        {
            new SearchSourceItem { Name = "Baidu", Keyword = "bd", Icon = "M9.154 0C7.71 0 6.54 1.658 6.54 3.707c0 2.051 1.171 3.71 2.615 3.71 1.446 0 2.614-1.659 2.614-3.71C11.768 1.658 10.6 0 9.154 0zm7.025.594C14.86.58 13.347 2.589 13.2 3.927c-.187 1.745.25 3.487 2.179 3.735 1.933.25 3.175-1.806 3.422-3.364.252-1.555-.995-3.364-2.362-3.674a1.218 1.218 0 0 0-.261-.03zM3.582 5.535a2.811 2.811 0 0 0-.156.008c-2.118.19-2.428 3.24-2.428 3.24-.287 1.41.686 4.425 3.297 3.864 2.617-.561 2.262-3.68 2.183-4.362-.125-1.018-1.292-2.773-2.896-2.75zm16.534 1.753c-2.308 0-2.617 2.119-2.617 3.616 0 1.43.121 3.425 2.988 3.362 2.867-.063 2.553-3.238 2.553-3.988 0-.745-.62-2.99-2.924-2.99zm-8.264 2.478c-1.424.014-2.708.925-3.565 2.483-1.642 2.986-.888 7.023 1.83 8.356 2.72 1.334 6.643-.092 7.733-3.084 1.092-2.993-.83-6.618-3.55-7.587a6.22 6.22 0 0 0-2.448-.168zm-.129 1.157c1.782.022 3.14 1.442 3.033 3.169-.107 1.727-1.637 3.092-3.418 3.07-1.781-.023-3.138-1.444-3.03-3.17.106-1.726 1.636-3.092 3.415-3.07z", Url = "https://www.baidu.com/s?wd=%s" },
            new SearchSourceItem { Name = "Google", Keyword = "g", Icon = "M12.24 10.285V14.4h6.887c-.648 2.41-2.519 4.114-5.136 4.114-3.535 0-6.4-2.865-6.4-6.4s2.865-6.4 6.4-6.4c1.582 0 3.02.574 4.136 1.518l3.12-3.12C19.094 2.14 15.918 1 12.24 1c-6.075 0-11 4.925-11 11s4.925 11 11 11c5.833 0 10.744-4.2 11.233-9.715H12.24z", Url = "https://www.google.com/search?q=%s" },
            new SearchSourceItem { Name = "Bing", Keyword = "bing", Icon = "M2.786 2.053a.5.5 0 0 0-.368.147l-1.62 1.62a.5.5 0 0 0 0 .707l7.734 7.734-2.822 1.54a.5.5 0 0 0-.26.438v7.26a.5.5 0 0 0 .74.437l11.084-6.046a.5.5 0 0 0 .26-.438V8.718a.5.5 0 0 0-.146-.353l-2.614-2.614a.5.5 0 0 0-.707 0L10.02 10.799 11.233 1.25a.5.5 0 0 0-.422-.556L3.208 2.067a.5.5 0 0 0-.422-.014z", Url = "https://www.bing.com/search?q=%s" },
            new SearchSourceItem { Name = "GitHub", Keyword = "gh", Icon = "M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12", Url = "https://github.com/search?q=%s" },
            new SearchSourceItem { Name = "Wikipedia", Keyword = "wiki", Icon = "M12.062 0C8.423.013 4.475 2.064 2.44 5.21c-.482.748-.823 1.57-.96 2.463l1.83.606c.465-1.472 1.488-2.726 2.83-3.44.053-.028.1-.06.155-.087-.193.593-.314 1.258-.337 1.956L4.72 9.475c-.218.618-.334 1.306-.316 2.03.048 1.92.98 3.548 2.302 4.44l.872-1.76a4.877 4.877 0 0 1-.954-2.68c0-.783.18-1.517.5-2.162l3.412 6.945c.42.06.852.095 1.293.095.347 0 .69-.023 1.026-.062l3.078-6.65c.348.65.545 1.4.545 2.2 0 1.282-.5 2.447-1.31 3.3l.995 1.8c1.378-.973 2.327-2.616 2.368-4.492.017-.768-.112-1.493-.348-2.146L21 8.52c-.053-.787-.234-1.53-.518-2.2-.016.028-.035.056-.05.085l-1.252 2.766c-.347-.69-.87-1.266-1.51-1.66l1.248-2.756c-.053-.053-.105-.108-.16-.16-1.554-1.464-3.69-2.39-6.05-2.535C12.483.02 12.274 0 12.062 0zm-7.69 8.272l1.637.54c.036-.312.1-.61.196-.893l-1.616-.534c-.092.28-.166.575-.216.887zm13.155 1.107l1.79.593c.12-.524.185-1.077.185-1.652 0-.306-.02-.607-.056-.9l-1.802.4c.026.173.036.353.036.536 0 .376-.054.733-.153 1.073zm-4.703.11c.753 0 1.36.626 1.36 1.4s-.607 1.4-1.36 1.4c-.752 0-1.36-.626-1.36-1.4s.608-1.4 1.36-1.4zm5.006.126l1.8.4c-.068-.58-.2-1.135-.393-1.657l-1.674.553c.11.223.2.46.267.704zM6.98 10.665c-.007.247.01.49.052.724l1.7-.565c-.02-.055-.034-.112-.047-.17l-1.7.01zm.937 2.47c.218.423.518.784.88 1.066l1.107-1.644c-.168-.1-.31-.225-.428-.372l-1.56.95zm5.556.76c.4 0 .72-.317.72-.71s-.32-.71-.72-.71c-.398 0-.72.317-.72.71s.322.71.72.71z", Url = "https://zh.wikipedia.org/wiki/Special:Search?search=%s" },
            new SearchSourceItem { Name = "YouTube", Keyword = "yt", Icon = "M23.498 6.163a3.003 3.003 0 0 0-2.11-2.11C19.517 3.545 12 3.545 12 3.545s-7.516 0-9.387.508a3.003 3.003 0 0 0-2.11 2.11C0 8.033 0 12 0 12s0 3.967.503 5.837a3.003 3.003 0 0 0 2.11 2.11c1.871.508 9.387.508 9.387.508s7.517 0 9.387-.508a3.003 3.003 0 0 0 2.11-2.11C24 15.967 24 12 24 12s0-3.967-.502-5.837zM9.545 15.568V8.432L15.818 12l-6.273 3.568z", Url = "https://www.youtube.com/results?search_query=%s" }
        };

    private static List<SearchSourceItem> LoadSearchSources()
    {
        if (_cachedSources != null)
        {
            return _cachedSources;
        }

        try
        {
            var sources = PluginSettingsService.GetSetting<List<SearchSourceItem>>(PluginId, "SearchSources", null!);
            if (sources != null && sources.Count > 0)
            {
                var defaults = GetDefaultSearchSources();
                foreach (var source in sources)
                {
                    if (source.Name == "Baidu" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M15.5 14h-.79") || source.Icon.StartsWith("M9.028 20.837")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Baidu").Icon;
                    }
                    else if (source.Name == "Bing" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M12 2C6.48") || source.Icon.StartsWith("M3.81 2c-.15")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Bing").Icon;
                    }
                    else if (source.Name == "Wikipedia" && (string.IsNullOrWhiteSpace(source.Icon) || source.Icon.StartsWith("M12 2C6.48") || source.Icon.StartsWith("M1.5 4h3.2")))
                    {
                        source.Icon = defaults.First(d => d.Name == "Wikipedia").Icon;
                    }
                }
                _cachedSources = sources;
                return sources;
            }
        }
        catch
        {
            // Fallback
        }

        if (_cachedSources == null)
        {
            _cachedSources = GetDefaultSearchSources();
        }
        return _cachedSources;
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrEmpty(query))
            yield break;

        var sources = LoadSearchSources();
        SearchSourceItem? matchedSource = null;
        var prefix = "";

        foreach (var src in sources)
        {
            var pfx = src.Keyword + " ";
            if (query.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            {
                matchedSource = src;
                prefix = pfx;
                break;
            }
        }

        if (matchedSource == null)
            yield break;

        var searchTerm = query.Substring(prefix.Length).Trim();

        var searchEngineName = !string.IsNullOrWhiteSpace(matchedSource.Name)
            ? matchedSource.Name
            : matchedSource.Keyword.ToUpperInvariant();

        var (iconData, iconColor) = GetIconInfo(matchedSource.Icon);

        if (string.IsNullOrEmpty(searchTerm))
        {
            yield return new InstantResultItem
            {
                Title = TranslationService.Format("WebSearch_PlaceholderTitle", searchEngineName),
                Description = TranslationService.Get("WebSearch_PlaceholderDesc"),
                IconData = iconData,
                IconColor = iconColor,
                ActionType = "None"
            };
            yield break;
        }

        var searchUrl = BuildUrl(matchedSource.Url, searchTerm);

        yield return new InstantResultItem
        {
            Title = TranslationService.Format("WebSearch_ResultTitle", searchEngineName, searchTerm),
            Description = TranslationService.Get("WebSearch_ResultDesc"),
            IconData = iconData,
            IconColor = iconColor,
            ActionType = "Execute",
            ActionArgument = searchUrl
        };

        if (string.IsNullOrWhiteSpace(matchedSource.SuggestUrl))
            yield break;

        var suggestionKey = matchedSource.Keyword + ":" + searchTerm;
        _latestSuggestionRequestKey = suggestionKey;

        List<string>? suggestions;
        lock (SuggestionCache)
        {
            SuggestionCache.TryGetValue(suggestionKey, out suggestions);
        }

        if (suggestions != null)
        {
            var shownCount = 0;
            foreach (var suggestion in suggestions)
            {
                if (shownCount >= MaxSuggestions)
                    break;

                if (string.Equals(suggestion, searchTerm, StringComparison.OrdinalIgnoreCase))
                    continue;

                shownCount++;
                yield return new InstantResultItem
                {
                    Title = suggestion,
                    Description = TranslationService.Format("WebSearch_SuggestionDesc", searchEngineName),
                    IconData = iconData,
                    IconColor = iconColor,
                    ActionType = "Execute",
                    ActionArgument = BuildUrl(matchedSource.Url, suggestion),
                    TabCompletion = prefix + suggestion
                };
            }
        }
        else
        {
            var shouldTrigger = false;
            lock (PendingSuggestionRequests)
            {
                if (!PendingSuggestionRequests.Contains(suggestionKey))
                {
                    PendingSuggestionRequests.Add(suggestionKey);
                    shouldTrigger = true;
                }
            }

            if (shouldTrigger)
            {
                TriggerSuggestionFetch(matchedSource, searchTerm, suggestionKey, prefix);
            }
        }
    }

    private static string BuildUrl(string template, string term)
    {
        var encoded = Uri.EscapeDataString(term);
        if (template.Contains("%s"))
        {
            return template.Replace("%s", encoded);
        }
        if (template.Contains("{0}"))
        {
            return string.Format(template, encoded);
        }
        return template + encoded;
    }

    private static void TriggerSuggestionFetch(SearchSourceItem source, string searchTerm, string suggestionKey, string prefix)
    {
        Task.Run(async () =>
        {
            var fetched = false;
            try
            {
                await Task.Delay(SuggestionDebounce);
                if (_latestSuggestionRequestKey != suggestionKey)
                {
                    // The user has already moved on to a different query; skip the network call.
                    return;
                }

                var suggestions = await FetchSuggestionsAsync(source.SuggestUrl, searchTerm);
                lock (SuggestionCache)
                {
                    SuggestionCache[suggestionKey] = suggestions;
                }
                fetched = true;
            }
            catch
            {
                lock (SuggestionCache)
                {
                    SuggestionCache[suggestionKey] = new List<string>();
                }
                fetched = true;
            }
            finally
            {
                lock (PendingSuggestionRequests)
                {
                    PendingSuggestionRequests.Remove(suggestionKey);
                }
            }

            if (fetched)
            {
                RefreshActiveSearches(prefix, searchTerm);
            }
        });
    }

    private static async Task<List<string>> FetchSuggestionsAsync(string suggestUrlTemplate, string searchTerm)
    {
        var url = BuildUrl(suggestUrlTemplate, searchTerm);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var cultureName = TranslationService.GetCurrentCulture();
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                request.Headers.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue(cultureName));
            }
            catch { }
        }

        using var response = await SuggestionHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseOpenSearchSuggestions(json);
    }

    private static List<string> ParseOpenSearchSuggestions(string json)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            return result;

        var suggestionsElement = root[1];
        if (suggestionsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in suggestionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }
        return result;
    }

    // Re-triggers active searches so they pick up newly-cached suggestions, via the host-provided
    // SearchRefreshService rather than reflecting into concrete App-side view model types.
    private static void RefreshActiveSearches(string prefix, string searchTerm)
    {
        SearchRefreshService.RefreshIfMatches(currentQueryText =>
            currentQueryText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentQueryText.Substring(prefix.Length).Trim(), searchTerm, StringComparison.OrdinalIgnoreCase));
    }

    private (string iconData, string iconColor) GetIconInfo(string iconNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(iconNameOrPath))
        {
            // Default search icon (magnifying glass)
            return ("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z", string.Empty);
        }

        return (iconNameOrPath.Trim(), string.Empty);
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var sources = LoadSearchSources();
        SearchSourceItem? matchedSource = null;
        var prefix = "";

        foreach (var src in sources)
        {
            var pfx = src.Keyword + " ";
            if (query.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
            {
                matchedSource = src;
                prefix = pfx;
                break;
            }
        }

        if (matchedSource == null) return null;
        var mask = new bool[text.Length];
        var searchTerm = query.Substring(prefix.Length).Trim();
        if (string.IsNullOrEmpty(searchTerm)) return mask;

        return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? mask;
    }
}
