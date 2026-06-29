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

    private readonly List<PluginSdk.Abstractions.Plugins.IAction> _plugins = new();
    private readonly List<PluginActionRegistration> _actions = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> _dynamicProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IInstantResultProvider> _instantResultProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> _searchableItemProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> _sidebarFilterProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IResultColumnProvider> _resultColumnProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ITranslationProvider> _translationProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IThemeProvider> _themeProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IActivePathCollector> _pathCollectors = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IFilePreviewProvider> _previewProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IQuickNavigationProvider> _quickNavigationProviders = new();
    private uint _nextRuntimeActionId = 0x80000000;

    private readonly ComponentFilter _filter = new();

    private PluginManager()
    {
        _filter.Refresh();

        // Wire up the dynamic filtering delegate for alias providers in the Core indexer
        AliasProviderRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.AliasProvider, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for active path collectors
        PluginSdk.Registries.ActivePathCollectorRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.ActivePathCollector, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for file dialog adapters
        PluginSdk.Registries.FileDialogAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.FileDialogAdapter, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for inline search adapters
        PluginSdk.Registries.InlineSearchAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.InlineSearchAdapter, prov.GetType().Name);

        // Wire up the settings delegate for plugins using the in-memory UserSettings cache
        PluginSdk.Services.PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            UserSettings.Load().GetPluginSetting(pluginId, key, defaultValue);

        // Wire up the history service delegate for plugins using Core SearchHistoryStore
        PluginSdk.Services.HistoryService.GetHistoryPathsFunc = () =>
            SearchHistoryStore.GetEntries();

        // Wire up the favorites service delegate for plugins using Core UserSettings
        PluginSdk.Services.FavoritesService.GetFavoritesFunc = () =>
            UserSettings.Load().Favorites.Select(f => new PluginSdk.Models.FavoriteItem { Name = f.Name, Path = f.Path });

        PluginLoader.Load(this);
    }

    // ── PluginRegistry callbacks ──────────────────────────────────────────

    void PluginRegistry.RegisterPlugin(PluginSdk.Abstractions.Plugins.IAction plugin) => RegisterPlugin(plugin);

    void PluginRegistry.AddInstantResultProvider(PluginSdk.Abstractions.Plugins.IInstantResultProvider p) => _instantResultProviders.Add(p);
    void PluginRegistry.AddSearchableItemProvider(PluginSdk.Abstractions.Plugins.ISearchableItemProvider p) => _searchableItemProviders.Add(p);
    void PluginRegistry.AddSidebarFilterProvider(PluginSdk.Abstractions.Plugins.ISidebarFilterProvider p) => _sidebarFilterProviders.Add(p);
    void PluginRegistry.AddResultColumnProvider(PluginSdk.Abstractions.Plugins.IResultColumnProvider p) => _resultColumnProviders.Add(p);
    void PluginRegistry.AddTranslationProvider(PluginSdk.Abstractions.Plugins.ITranslationProvider p) => _translationProviders.Add(p);
    void PluginRegistry.AddThemeProvider(PluginSdk.Abstractions.Plugins.IThemeProvider p) => _themeProviders.Add(p);
    void PluginRegistry.AddActivePathCollector(PluginSdk.Abstractions.Plugins.IActivePathCollector p)
    {
        _pathCollectors.Add(p);
        PluginSdk.Registries.ActivePathCollectorRegistry.Register(p);
    }
    void PluginRegistry.AddFilePreviewProvider(PluginSdk.Abstractions.Plugins.IFilePreviewProvider p) => _previewProviders.Add(p);
    void PluginRegistry.AddQuickNavigationProvider(PluginSdk.Abstractions.Plugins.IQuickNavigationProvider p) => _quickNavigationProviders.Add(p);

    // ── Public API ────────────────────────────────────────────────────────

    public void RefreshDisabledComponents() => _filter.Refresh();

    public bool IsComponentEnabled(string dllName, PluginComponentType type, string name)
        => _filter.IsEnabled(dllName, type, name);

    /// <summary>Registers a plugin and loads its actions and dynamic providers.</summary>
    public void RegisterPlugin(PluginSdk.Abstractions.Plugins.IAction plugin)
    {
        if (plugin == null) return;
        _plugins.Add(plugin);
        foreach (var action in plugin.GetActions())
            _actions.Add(new PluginActionRegistration(_nextRuntimeActionId++, plugin, action));
        foreach (var provider in plugin.GetDynamicProviders())
            _dynamicProviders.Add(provider);
    }

    // ── Filtered collections (active components only) ─────────────────────

    public IEnumerable<PluginSdk.Abstractions.Plugins.IAction> Plugins => _plugins;

    public IEnumerable<PluginActionRegistration> Actions
        => _actions.Where(a => _filter.IsEnabled(ComponentFilter.GetDllName(a.Plugin), PluginComponentType.Action, a.Action.Id));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> DynamicProviders
        => _dynamicProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.DynamicProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IQuickNavigationProvider> QuickNavigationProviders
        => _quickNavigationProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QuickNavigationProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IQuickNavigationProvider> AllQuickNavigationProviders => _quickNavigationProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> InstantResultProviders
        => _instantResultProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.InstantProvider, p.Id));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> SearchableItemProviders
        => _searchableItemProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.SearchableItemProvider, p.Id));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> SidebarFilterProviders
    {
        get
        {
            foreach (var p in _sidebarFilterProviders)
                yield return new FilteredSidebarFilterProvider(p, ComponentFilter.GetDllName(p), this);
        }
    }

    public IEnumerable<PluginSdk.Abstractions.Plugins.IResultColumnProvider> ResultColumnProviders
    {
        get
        {
            foreach (var p in _resultColumnProviders)
                yield return new FilteredResultColumnProvider(p, ComponentFilter.GetDllName(p), this);
        }
    }

    public IEnumerable<PluginSdk.Abstractions.Plugins.ITranslationProvider> TranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IThemeProvider> ThemeProviders => _themeProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IActivePathCollector> ActivePathCollectors => _pathCollectors;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IFilePreviewProvider> FilePreviewProviders
        => _previewProviders
            .Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.FilePreviewProvider, p.GetType().Name))
            .OrderByDescending(p => p.Priority);

    // ── Unfiltered collections (settings UI ?show disabled as unchecked) ─

    public IEnumerable<PluginSdk.Abstractions.Plugins.IFilePreviewProvider> AllFilePreviewProviders => _previewProviders;

    public IEnumerable<PluginActionRegistration> AllActions => _actions;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> AllDynamicProviders => _dynamicProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> AllInstantResultProviders => _instantResultProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> AllSearchableItemProviders => _searchableItemProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> AllSidebarFilterProviders => _sidebarFilterProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IResultColumnProvider> AllResultColumnProviders => _resultColumnProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ITranslationProvider> AllTranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IThemeProvider> AllThemeProviders => _themeProviders;

    // ── Search and execution ──────────────────────────────────────────────

    public IEnumerable<PluginSearchActionMatch> SearchActionItems(string query, PluginSdk.Abstractions.SearchWindowType windowType, string? contextDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        if (windowType == PluginSdk.Abstractions.SearchWindowType.Inline && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog) yield break;

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

    public bool TryExecuteSearchAction(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view)
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

internal class SimpleSearchResult : PluginSdk.Abstractions.ISearchResult
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string ContextDirectory { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public bool IsApplication { get; set; }
    public DateTime DateModified { get; set; } = DateTime.Now;
}
