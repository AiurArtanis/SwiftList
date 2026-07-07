using System.IO;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Search;

// One entry per candidate tab, built fresh on every StartupPanelController activation. A source that
// yields zero items is simply left out of the tab strip -- see StartupPanelController.TryActivateAsync.
internal interface ITabSource
{
    string Label { get; }

    // Hides this tab from the panel going forward (the x button). Deliberately distinct from a plugin
    // component being disabled -- see PluginTabSource.Close for why the two must not share storage.
    void Close();
    Task<List<AppSearchResult>> LoadItemsAsync();
}

// The built-in "Recent Files" tab -- distinct from the plugin-provided sources below because it needs
// its own dedicated Settings sub-page (target directories, count, max age) and an IPC round-trip to
// the indexing service, neither of which fits the plugin model's "just hand back items" contract.
internal sealed class RecentFilesTabSource : ITabSource
{
    private readonly SearchService _searchService;

    public RecentFilesTabSource(SearchService searchService) => _searchService = searchService;

    public string Label => TranslationManager.Instance["StartupPanel_TabRecentFiles"];

    public void Close()
    {
        var settings = UserSettings.Load();
        settings.StartupPanel.RecentFilesEnabled = false;
        settings.Save();
    }

    public async Task<List<AppSearchResult>> LoadItemsAsync()
    {
        var panelSettings = UserSettings.Load().StartupPanel;
        if (panelSettings.RecentFilesDirectories.Count == 0)
            return new List<AppSearchResult>();

        var recentFiles = await _searchService.GetRecentFilesAsync(
            panelSettings.RecentFilesDirectories, panelSettings.RecentFilesCount, panelSettings.RecentFilesMaxAgeMinutes);

        var uiResults = new List<AppSearchResult>(recentFiles.Count);
        for (var i = 0; i < recentFiles.Count; i++)
            uiResults.Add(SearchResultHelper.CreateUiResult(recentFiles[i], string.Empty, i, isApplication: false, scope: null));
        return uiResults;
    }
}

// Wraps a plugin-contributed IStartupPanelTabProvider (see PluginSdk.Abstractions.Plugins). Closing this
// tab is a panel-local "don't show it for now" choice, not a plugin-level decision -- it writes to
// StartupPanel.ClosedTabIds, never to UserSettings.DisabledPluginComponents. That other list is a load-
// time gate: a provider disabled there never becomes a candidate tab at all (see
// PluginManager.StartupPanelTabProviders), so it can't reach this class in the first place. Conflating
// the two would mean closing one tab in the live panel silently re-labels the whole plugin component as
// "disabled" in the unrelated Plugin Management settings page.
internal sealed class PluginTabSource : ITabSource
{
    private readonly PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider _provider;

    public PluginTabSource(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider provider) => _provider = provider;

    public string Label => _provider.Name;

    // Shared with StartupPanelPluginTabViewModel, which reads/writes the same ClosedTabIds entries so
    // the Settings page's "reopen" checkboxes and this tab's x button agree on identity.
    public static string ComponentId(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider provider)
        => $"{ComponentFilter.GetDllName(provider)}::{PluginComponentType.StartupPanelTabProvider}::{provider.Id}";

    public void Close()
    {
        var settings = UserSettings.Load();
        var id = ComponentId(_provider);
        if (settings.StartupPanel.ClosedTabIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            return;

        settings.StartupPanel.ClosedTabIds.Add(id);
        settings.Save();
    }

    public Task<List<AppSearchResult>> LoadItemsAsync()
    {
        try
        {
            var items = _provider.GetItems()
                .Select((item, index) => MapToUiResult(item, index))
                .ToList();
            return Task.FromResult(items);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginTabSource] {_provider.GetType().Name}.GetItems() failed: {ex.Message}", LogLevel.Error);
            return Task.FromResult(new List<AppSearchResult>());
        }
    }

    private static AppSearchResult MapToUiResult(PluginSdk.Abstractions.ISearchResult item, int index)
    {
        // A web-address favorite isn't a real filesystem path -- Path.GetDirectoryName mangles it (e.g.
        // "https://www.google.com" becomes "https:"), and there's no shell icon to look up for it either.
        var isWebUrl = FavoriteUrlHelper.IsWebUrl(item.FullPath);
        // FormatWslPath renders "\\wsl$\Ubuntu\..." as "WSL-Ubuntu:/..." -- the same format regular search
        // already shows for WSL results (see SearchResultHelper.GetParentDisplayText), so a WSL favorite/
        // history entry doesn't display differently just because it came through this tab instead.
        var parentDir = isWebUrl ? item.FullPath : SearchResultHelper.FormatWslPath(Path.GetDirectoryName(item.FullPath) ?? string.Empty);
        return new AppSearchResult
        {
            Name = item.Name,
            FullPath = item.FullPath,
            ParentDir = parentDir,
            ContextDirectory = item.ContextDirectory,
            IsDir = item.IsDir,
            Drive = string.IsNullOrEmpty(item.FullPath) ? string.Empty : (Path.GetPathRoot(item.FullPath) ?? string.Empty).TrimEnd('\\'),
            ResultKind = item.IsApplication ? "Application" : "File",
            Index = index,
            IconOverride = isWebUrl ? FavoriteUrlHelper.Icon : null
        };
    }
}
