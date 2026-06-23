using SwiftList.Core;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

public static class PluginSearchResultMapper
{
    public static void AddInstantResults(List<AppSearchResult> uiResults, string query, bool isInlineWindow)
    {
        if (isInlineWindow)
            return;

        foreach (var provider in PluginManager.Instance.InstantResultProviders)
        {
            try
            {
                var results = provider.GetInstantResults(query);
                if (results == null)
                    continue;

                foreach (var item in results)
                {
                    if (item == null)
                        continue;

                    System.Windows.Media.ImageSource? iconOverride = null;
                    if (!string.IsNullOrWhiteSpace(item.IconData))
                    {
                        try
                        {
                            var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                            iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[SearchResultMapper] Failed to create vector icon for instant result: {ex.Message}", LogLevel.Error);
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

                    uiResults.Add(new AppSearchResult
                    {
                        Name = item.Title,
                        FullPath = $"__INSTANT_RESULT__:{provider.Name}:{item.Title}",
                        ParentDir = item.Description,
                        IsDir = false,
                        Drive = string.Empty,
                        ResultKind = "InstantResult",
                        Index = uiResults.Count,
                        SearchQuery = query,
                        IconOverride = iconOverride,
                        InstantResultActionType = item.ActionType ?? "Copy",
                        InstantResultActionArgument = item.ActionArgument ?? string.Empty,
                        TabCompletion = item.TabCompletion,
                        SourceProvider = provider
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchResultMapper] Error getting instant results from provider '{provider.Name}': {ex.Message}", LogLevel.Error);
            }
        }
    }

    public static bool AddPluginSearchActionResults(List<AppSearchResult> uiResults, string query, string? contextDirectory, bool isInlineWindow)
    {
        string? currentGroup = null;
        var added = false;
        var windowType = isInlineWindow ? PluginSdk.SearchWindowType.Inline : PluginSdk.SearchWindowType.Main;
        foreach (var match in PluginManager.Instance.SearchActionItems(query, windowType, contextDirectory))
        {
            var action = match.Registration.Action;
            var group = string.IsNullOrWhiteSpace(action.GroupName) ? TranslationManager.Instance["Action_DefaultGroup"] : action.GroupName;
            if (!string.Equals(currentGroup, group, StringComparison.Ordinal))
            {
                SearchResultMapper.AddSectionHeader(uiResults, group, query);
                currentGroup = group;
            }

            uiResults.Add(new AppSearchResult
            {
                Name = action.DisplayName,
                FullPath = $"__PLUGIN_ACTION__:{match.Registration.RuntimeActionId}",
                ParentDir = BuildPluginActionHint(match.Keyword, action.Parameters),
                ContextDirectory = contextDirectory ?? string.Empty,
                IsDir = false,
                Drive = string.Empty,
                ResultKind = "PluginAction",
                Index = uiResults.Count,
                SearchQuery = query,
                PluginActionId = match.Registration.RuntimeActionId,
                PluginActionArgumentText = match.ArgumentText,
                IconOverride = action.Icon,
                SourceProvider = match.Registration.Plugin
            });
            added = true;
        }

        return added;
    }

    public static string BuildPluginActionHint(string keyword, IReadOnlyList<string> parameters)
    {
        if (parameters.Count == 0)
            return string.Format(TranslationManager.Instance["Search_KeywordOnly"], keyword);

        return string.Format(TranslationManager.Instance["Search_KeywordParams"], keyword, string.Join(" ", parameters));
    }
}
