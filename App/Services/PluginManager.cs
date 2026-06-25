using SwiftList.Core;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.App.Services.PluginManagerCore;

namespace SwiftList.App.Services;

/// <summary>
/// Central hub for plugin lifecycle management: loading, registration,
/// filtering by enabled state, search action dispatch, and instant result execution.
/// <para>
/// Loading is delegated to <see cref="PluginLoader"/>;
/// component enable/disable state is managed by <see cref="ComponentFilter"/>.
/// </para>
/// </summary>
public class PluginManager : PluginRegistry
{
    private static readonly Lazy<PluginManager> _instance = new(() => new PluginManager());

    /// <summary>Gets the singleton instance of the PluginManager.</summary>
    public static PluginManager Instance => _instance.Value;

    private readonly List<PluginSdk.IActionPlugin> _plugins = new();
    private readonly List<PluginActionRegistration> _actions = new();
    private readonly List<PluginSdk.IDynamicActionProvider> _dynamicProviders = new();
    private readonly List<PluginSdk.IInstantResultProvider> _instantResultProviders = new();
    private readonly List<PluginSdk.ISearchableItemProvider> _searchableItemProviders = new();
    private readonly List<PluginSdk.ISidebarFilterProvider> _sidebarFilterProviders = new();
    private readonly List<PluginSdk.IResultColumnProvider> _resultColumnProviders = new();
    private readonly List<PluginSdk.ITranslationProvider> _translationProviders = new();
    private readonly List<PluginSdk.IThemeProvider> _themeProviders = new();
    private readonly List<PluginSdk.IActivePathCollector> _pathCollectors = new();
    private readonly List<PluginSdk.IFilePreviewProvider> _previewProviders = new();
    private readonly List<PluginSdk.IQuickNavigationProvider> _quickNavigationProviders = new();
    private uint _nextRuntimeActionId = 0x80000000;

    private readonly ComponentFilter _filter = new();

    private PluginManager()
    {
        _filter.Refresh();

        // Wire up the dynamic filtering delegate for alias providers in the Core indexer
        AliasProviderRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.AliasProvider, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for active path collectors
        PluginSdk.ActivePathCollectorRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.ActivePathCollector, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for file dialog adapters
        PluginSdk.FileDialogAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.FileDialogAdapter, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for inline search adapters
        PluginSdk.InlineSearchAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.InlineSearchAdapter, prov.GetType().Name);

        // Wire up the settings delegate for plugins using the in-memory UserSettings cache
        PluginSdk.PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            UserSettings.Load().GetPluginSetting(pluginId, key, defaultValue);

        // Wire up the history service delegate for plugins using Core SearchHistoryStore
        PluginSdk.HistoryService.GetHistoryPathsFunc = () =>
            SearchHistoryStore.GetEntries();

        // Wire up the favorites service delegate for plugins using Core UserSettings
        PluginSdk.FavoritesService.GetFavoritesFunc = () =>
            UserSettings.Load().Favorites.Select(f => new PluginSdk.FavoriteItem { Name = f.Name, Path = f.Path });

        PluginLoader.Load(this);
    }

    // ── PluginRegistry callbacks ──────────────────────────────────────────

    void PluginRegistry.RegisterPlugin(PluginSdk.IActionPlugin plugin) => RegisterPlugin(plugin);

    void PluginRegistry.AddInstantResultProvider(PluginSdk.IInstantResultProvider p) => _instantResultProviders.Add(p);
    void PluginRegistry.AddSearchableItemProvider(PluginSdk.ISearchableItemProvider p) => _searchableItemProviders.Add(p);
    void PluginRegistry.AddSidebarFilterProvider(PluginSdk.ISidebarFilterProvider p) => _sidebarFilterProviders.Add(p);
    void PluginRegistry.AddResultColumnProvider(PluginSdk.IResultColumnProvider p) => _resultColumnProviders.Add(p);
    void PluginRegistry.AddTranslationProvider(PluginSdk.ITranslationProvider p) => _translationProviders.Add(p);
    void PluginRegistry.AddThemeProvider(PluginSdk.IThemeProvider p) => _themeProviders.Add(p);
    void PluginRegistry.AddActivePathCollector(PluginSdk.IActivePathCollector p)
    {
        _pathCollectors.Add(p);
        PluginSdk.ActivePathCollectorRegistry.Register(p);
    }
    void PluginRegistry.AddFilePreviewProvider(PluginSdk.IFilePreviewProvider p) => _previewProviders.Add(p);
    void PluginRegistry.AddQuickNavigationProvider(PluginSdk.IQuickNavigationProvider p) => _quickNavigationProviders.Add(p);

    // ── Public API ────────────────────────────────────────────────────────

    public void RefreshDisabledComponents() => _filter.Refresh();

    public bool IsComponentEnabled(string dllName, PluginComponentType type, string name)
        => _filter.IsEnabled(dllName, type, name);

    /// <summary>Registers a plugin and loads its actions and dynamic providers.</summary>
    public void RegisterPlugin(PluginSdk.IActionPlugin plugin)
    {
        if (plugin == null) return;
        _plugins.Add(plugin);
        foreach (var action in plugin.GetActions())
            _actions.Add(new PluginActionRegistration(_nextRuntimeActionId++, plugin, action));
        foreach (var provider in plugin.GetDynamicProviders())
            _dynamicProviders.Add(provider);
    }

    // ── Filtered collections (active components only) ─────────────────────

    public IEnumerable<PluginSdk.IActionPlugin> Plugins => _plugins;

    public IEnumerable<PluginActionRegistration> Actions
        => _actions.Where(a => _filter.IsEnabled(ComponentFilter.GetDllName(a.Plugin), PluginComponentType.Action, a.Action.Id));

    public IEnumerable<PluginSdk.IDynamicActionProvider> DynamicProviders
        => _dynamicProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.DynamicProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.IQuickNavigationProvider> QuickNavigationProviders
        => _quickNavigationProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QuickNavigationProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.IQuickNavigationProvider> AllQuickNavigationProviders => _quickNavigationProviders;

    public IEnumerable<PluginSdk.IInstantResultProvider> InstantResultProviders
        => _instantResultProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.InstantProvider, p.Id));

    public IEnumerable<PluginSdk.ISearchableItemProvider> SearchableItemProviders
        => _searchableItemProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.SearchableItemProvider, p.Id));

    public IEnumerable<PluginSdk.ISidebarFilterProvider> SidebarFilterProviders
    {
        get
        {
            foreach (var p in _sidebarFilterProviders)
                yield return new FilteredSidebarFilterProvider(p, ComponentFilter.GetDllName(p), this);
        }
    }

    public IEnumerable<PluginSdk.IResultColumnProvider> ResultColumnProviders
    {
        get
        {
            foreach (var p in _resultColumnProviders)
                yield return new FilteredResultColumnProvider(p, ComponentFilter.GetDllName(p), this);
        }
    }

    public IEnumerable<PluginSdk.ITranslationProvider> TranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.IThemeProvider> ThemeProviders => _themeProviders;
    public IEnumerable<PluginSdk.IActivePathCollector> ActivePathCollectors => _pathCollectors;
    public IEnumerable<PluginSdk.IFilePreviewProvider> FilePreviewProviders
        => _previewProviders
            .Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.FilePreviewProvider, p.GetType().Name))
            .OrderByDescending(p => p.Priority);

    // ── Unfiltered collections (settings UI ?show disabled as unchecked) ─

    public IEnumerable<PluginSdk.IFilePreviewProvider> AllFilePreviewProviders => _previewProviders;

    public IEnumerable<PluginActionRegistration> AllActions => _actions;
    public IEnumerable<PluginSdk.IDynamicActionProvider> AllDynamicProviders => _dynamicProviders;
    public IEnumerable<PluginSdk.IInstantResultProvider> AllInstantResultProviders => _instantResultProviders;
    public IEnumerable<PluginSdk.ISearchableItemProvider> AllSearchableItemProviders => _searchableItemProviders;
    public IEnumerable<PluginSdk.ISidebarFilterProvider> AllSidebarFilterProviders => _sidebarFilterProviders;
    public IEnumerable<PluginSdk.IResultColumnProvider> AllResultColumnProviders => _resultColumnProviders;
    public IEnumerable<PluginSdk.ITranslationProvider> AllTranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.IThemeProvider> AllThemeProviders => _themeProviders;

    // ── Search and execution ──────────────────────────────────────────────

    public IEnumerable<PluginSearchActionMatch> SearchActionItems(string query, PluginSdk.SearchWindowType windowType, string? contextDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        if (windowType == PluginSdk.SearchWindowType.Inline && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog) yield break;

        var tempResult = new SimpleSearchResult
        {
            ContextDirectory = contextDirectory ?? string.Empty,
            FullPath = string.Empty,
            IsDir = false
        };

        foreach (var action in _actions)
        {
            if (action.Action.Keywords.Count == 0) continue;
            if (!action.Action.IsVisibleInSearch(tempResult, windowType)) continue;
            if (!_filter.IsEnabled(ComponentFilter.GetDllName(action.Plugin), PluginComponentType.Action, action.Action.Id)) continue;
            if (!action.Action.CanExecute(tempResult)) continue;

            var match = KeywordMatcher.TryMatchKeyword(query, action.Action.Keywords);
            if (match == null) continue;

            yield return new PluginSearchActionMatch(action, match.Value.Keyword, match.Value.ArgumentText);
        }
    }

    public bool TryExecuteSearchAction(AppSearchResult result, PluginSdk.IPluginSearchWindow view)
    {
        if (result.IsInstantResult)
        {
            try
            {
                if (result.InstantResultOnExecute != null)
                {
                    result.InstantResultOnExecute();
                }
                else if (result.InstantResultActionType == "Copy")
                    System.Windows.Clipboard.SetText(result.InstantResultActionArgument);
                else if (result.InstantResultActionType == "Execute")
                {
                    var arg = result.InstantResultActionArgument.Trim();
                    var runAsAdmin = false;
                    if (arg.StartsWith("runas:", StringComparison.OrdinalIgnoreCase))
                    {
                        runAsAdmin = true;
                        arg = arg.Substring(6).Trim();
                    }

                    var fileName = arg;
                    var arguments = "";
                    if (arg.StartsWith("\""))
                    {
                        var endQuote = arg.IndexOf('\"', 1);
                        if (endQuote > 0)
                        {
                            fileName = arg.Substring(1, endQuote - 1);
                            arguments = arg.Substring(endQuote + 1).Trim();
                        }
                    }
                    else
                    {
                        var firstSpace = arg.IndexOf(' ');
                        if (firstSpace > 0)
                        {
                            fileName = arg.Substring(0, firstSpace);
                            arguments = arg.Substring(firstSpace + 1).Trim();
                        }
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };
                    if (runAsAdmin)
                    {
                        psi.Verb = "runas";
                    }
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginManager] Failed to execute instant result action: {ex.Message}", LogLevel.Error);
            }
            return true;
        }

        if (!result.IsPluginSearchAction || result.IsSearchSectionHeader) return false;

        var registration = _actions.FirstOrDefault(x => x.RuntimeActionId == result.PluginActionId);
        if (registration == null)
        {
            Logger.Log($"[PluginManager] Plugin search action not found: {result.PluginActionId}", LogLevel.Warn);
            return false;
        }

        registration.Action.Execute(
            new PluginSearchResult(result.Name, result.PluginActionArgumentText, result.ContextDirectory), view);
        return true;
    }

    public PluginActionRegistration? GetActionByRuntimeId(uint runtimeActionId)
        => _actions.FirstOrDefault(x => x.RuntimeActionId == runtimeActionId);
}

internal class SimpleSearchResult : PluginSdk.ISearchResult
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string ContextDirectory { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public bool IsApplication { get; set; }
    public DateTime DateModified { get; set; } = DateTime.Now;
}
