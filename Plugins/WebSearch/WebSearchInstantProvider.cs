using SwiftList.PluginSdk;

namespace SwiftList.Plugins.WebSearch;

public class WebSearchInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WebSearch_ProviderName");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrEmpty(query))
            yield break;

        var prefix = "";
        var searchEngineName = "";
        var searchUrlTemplate = "";
        var iconData = "";
        var iconColor = "";

        if (query.StartsWith("gg ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "gg ";
            searchEngineName = "Google";
            searchUrlTemplate = "https://www.google.com/search?q={0}";
            iconData = "M12.24 10.285V14.4h6.887c-.648 2.41-2.519 4.114-5.136 4.114-3.535 0-6.4-2.865-6.4-6.4s2.865-6.4 6.4-6.4c1.582 0 3.02.574 4.136 1.518l3.12-3.12C19.094 2.14 15.918 1 12.24 1c-6.075 0-11 4.925-11 11s4.925 11 11 11c5.833 0 10.744-4.2 11.233-9.715H12.24z";
            iconColor = "#EA4335";
        }
        else if (query.StartsWith("bd ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "bd ";
            searchEngineName = TranslationService.Get("WebSearch_Baidu") ?? "百度";
            searchUrlTemplate = "https://www.baidu.com/s?wd={0}";
            iconData = "M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z";
            iconColor = "#2932E1";
        }
        else if (query.StartsWith("bing ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "bing ";
            searchEngineName = "Bing";
            searchUrlTemplate = "https://www.bing.com/search?q={0}";
            iconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
            iconColor = "#00809D";
        }
        else if (query.StartsWith("gh ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "gh ";
            searchEngineName = "GitHub";
            searchUrlTemplate = "https://github.com/search?q={0}";
            iconData = "M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12";
            iconColor = "#24292e";
        }
        else if (query.StartsWith("wiki ", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "wiki ";
            searchEngineName = "Wikipedia";
            searchUrlTemplate = "https://zh.wikipedia.org/wiki/Special:Search?search={0}";
            iconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
            iconColor = "#666666";
        }
        else
        {
            yield break;
        }

        var keyword = query.Substring(prefix.Length).Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            yield return new InstantResultItem
            {
                Title = string.Format(TranslationService.Get("WebSearch_PlaceholderTitle") ?? "使用 {0} 搜索", searchEngineName),
                Description = TranslationService.Get("WebSearch_PlaceholderDesc") ?? "输入关键字并回车进行搜索",
                IconData = iconData,
                IconColor = iconColor,
                ActionType = "None"
            };
            yield break;
        }

        var searchUrl = string.Format(searchUrlTemplate, Uri.EscapeDataString(keyword));
        yield return new InstantResultItem
        {
            Title = string.Format(TranslationService.Get("WebSearch_ResultTitle") ?? "使用 {0} 搜索: {1}", searchEngineName, keyword),
            Description = TranslationService.Get("WebSearch_ResultDesc") ?? "回车或单击直接在浏览器中打开搜索结果",
            IconData = iconData,
            IconColor = iconColor,
            ActionType = "Execute",
            ActionArgument = searchUrl
        };
    }
}
