using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

// Startup-panel tab backed by the host's own search history (HistoryService, already wired to
// SwiftList.Core.SearchHistoryStore, most-recent-first). Capped at 20 -- unlike Recent Files, there's
// no dedicated settings page for this tab to make that configurable.
public class HistoryTabProvider : IStartupPanelTabProvider
{
    private const int MaxItems = 20;

    public string Name => TranslationService.Get("StartupPanel_TabHistory");

    public IEnumerable<ISearchResult> GetItems() => HistoryService.GetHistoryPaths()
        .Where(p => HistoryService.IsAppEntry(p) || File.Exists(p) || Directory.Exists(p))
        .Take(MaxItems)
        .Select(p => (ISearchResult)new StartupPanelResultItem(HistoryService.GetRawPath(p)));
}
