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

// The built-in "Last Directory" tab -- lists the contents of whatever folder a native file dialog (any
// app's, not just SwiftList's own UI) was last navigated to while SwiftList's dialog-interception hook
// was tracking it. Reads InlineSearchManager's own ExplorerTracker, which mirrors the Hook process's
// live state over IPC (see InlineSearchManager's ctor) -- no separate round trip needed since that
// tracker is already running by the time the quick window (and this tab) can exist.
internal sealed class LastDirectoryTabSource : ITabSource
{
    // A folder can have far more entries than a startup-panel tab should ever show at once.
    private const int MaxItems = 100;

    public string Label => TranslationManager.Instance["StartupPanel_TabLastDirectory"];

    public void Close()
    {
        var settings = UserSettings.Load();
        settings.StartupPanel.LastDirectoryEnabled = false;
        settings.Save();
    }

    public Task<List<AppSearchResult>> LoadItemsAsync()
    {
        var path = InlineSearchManager.Instance.ExplorerTracker.LastActiveExplorerPath;
        // The Desktop is where Explorer/dialogs land by default when nothing more specific has been
        // browsed to yet, so treating it as "last visited" would make this tab show up constantly with
        // a location the user didn't actually navigate to.
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || IsCurrentUserDesktop(path))
            return Task.FromResult(new List<AppSearchResult>());

        var uiResults = new List<AppSearchResult>();
        try
        {
            var drive = (Path.GetPathRoot(path) ?? string.Empty).TrimEnd('\\', ':');
            var exclusions = ExclusionRuleSet.From(UserSettings.Load());
            var index = 0;
            // A raw filesystem walk sees everything, unlike the real index (which skips hidden/system
            // entries and anything the user has excluded) -- apply the same two filters here so this
            // tab doesn't surface things like $RECYCLE.BIN that a normal search never would.
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if (index >= MaxItems)
                    break;
                if (FileSystemItemFilter.IsHiddenOrSystem(entry.Attributes))
                    continue;

                var isDir = entry is DirectoryInfo;
                if (exclusions.IsExcludedPath(entry.FullName, isDir))
                    continue;

                var item = new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    IsDir = isDir,
                    Drive = drive,
                    Attributes = entry.Attributes,
                    ModifiedUtc = FileTimeHelper.ToUnixSeconds(entry.LastWriteTimeUtc)
                };
                uiResults.Add(SearchResultHelper.CreateUiResult(item, string.Empty, index, isApplication: false, scope: null));
                index++;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LastDirectoryTabSource] Failed to list '{path}': {ex.Message}", LogLevel.Error);
            return Task.FromResult(new List<AppSearchResult>());
        }
        return Task.FromResult(uiResults);
    }

    private static bool IsCurrentUserDesktop(string path)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.Equals(path.TrimEnd('\\'), desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
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
