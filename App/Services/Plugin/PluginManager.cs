using SwiftList.Core;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.PluginSdk.Abstractions.Plugins.Preview;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;

using SwiftList.Core.SearchIndex;
namespace SwiftList.App.Services.Plugin;

/// <summary>
/// Central hub for plugin lifecycle management: loading, registration,
/// filtering by enabled state, search action dispatch, and instant result execution.
/// <para>
/// Loading is delegated to <see cref="PluginLoader"/>;
/// component enable/disable state is managed by <see cref="ComponentFilter"/>.
/// </para>
/// </summary>
public partial class PluginManager : PluginRegistry
{
    private static readonly Lazy<PluginManager> _instance = new(() => new PluginManager());

    /// <summary>Gets the singleton instance of the PluginManager.</summary>
    public static PluginManager Instance => _instance.Value;

    private readonly List<PluginSdk.Abstractions.Plugins.IPlugin> _plugins = new();
    private readonly List<PluginActionRegistration> _actions = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> _dynamicActionProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IInstantResultProvider> _instantResultProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> _searchableItemProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> _sidebarFilterProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IResultColumnProvider> _resultColumnProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ITranslationProvider> _translationProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IThemeProvider> _themeProviders = new();
    private readonly List<IActivePathCollector> _pathCollectors = new();
    private readonly List<IFilePreviewProvider> _previewProviders = new();
    private readonly List<IQuickNavigationProvider> _quickNavigationProviders = new();
    private readonly List<IThumbnailProvider> _thumbnailProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> _queryTokenProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider> _startupPanelTabProviders = new();
    private uint _nextRuntimeActionId = 0x80000000;

    // pluginId -> (field Key -> schema DefaultValue), built once after all plugins are loaded, so
    // GetSettingFunc can fall back to a plugin's own declared default without every call site needing
    // to duplicate it in code. See PluginLoaderHelper.BuildSchemaDefaultsMap.
    private Dictionary<string, Dictionary<string, object?>> _pluginSchemaDefaults = new(StringComparer.OrdinalIgnoreCase);

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

        // Bridges PluginSdk's own static service delegates (settings, history, favorites, fuzzy-match,
        // highlight-mask, directory search) to their Core/App implementations -- none of that is plugin
        // lifecycle itself, so it lives in its own class rather than this constructor.
        PluginSdkBridge.Initialize(this);

        PluginLoader.Load(this);

        // Must run after PluginLoader.Load so every plugin's IConfigurable is discoverable; must pass
        // `this` rather than PluginManager.Instance since Instance's Lazy<T> is still initializing here.
        _pluginSchemaDefaults = Helpers.PluginLoaderHelper.BuildSchemaDefaultsMap(this);
    }

    // ── Public API ────────────────────────────────────────────────────────

    // Raised so callers that cache anything derived from IsEnabled-filtered collections (e.g.
    // StartupPanelTabProviders, which RefreshDisabledComponents can change the membership of) know to
    // invalidate -- see App.xaml.cs's SettingsSearchService.GetEntriesFunc cache.
    public event Action? ComponentsRefreshed;

    public void RefreshDisabledComponents()
    {
        _filter.Refresh();
        ComponentsRefreshed?.Invoke();
    }

    public bool IsComponentEnabled(string dllName, PluginComponentType type, string name)
        => _filter.IsEnabled(dllName, type, name);

    /// <summary>Registers a plugin and loads its actions and dynamic providers.</summary>
    public void RegisterPlugin(PluginSdk.Abstractions.Plugins.IPlugin plugin)
    {
        if (plugin == null) return;
        _plugins.Add(plugin);
        if (plugin is PluginSdk.Abstractions.Plugins.IActionProvider actionProvider)
        {
            foreach (var action in actionProvider.GetActions())
                _actions.Add(new PluginActionRegistration(_nextRuntimeActionId++, plugin, action));
            foreach (var provider in actionProvider.GetDynamicActionProviders())
                _dynamicActionProviders.Add(provider);
        }
    }

    // ── Filtered collections (active components only) ─────────────────────

    public IEnumerable<PluginSdk.Abstractions.Plugins.IPlugin> Plugins => _plugins;

    public IEnumerable<PluginActionRegistration> Actions
        => _actions.Where(a => _filter.IsEnabled(ComponentFilter.GetDllName(a.Plugin), PluginComponentType.Action, a.Action.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> DynamicActionProviders
        => _dynamicActionProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.DynamicActionProvider, p.GetType().Name));

    // Ordered per UserSettings.QuickNavigationProviderOrder (position = priority, most-preferred
    // first); a provider whose id isn't listed there yet falls back to int.MaxValue, which -- since
    // LINQ's OrderBy is a stable sort -- lands it after every listed provider while preserving its
    // original discovery-order position relative to any OTHER unlisted provider, rather than an
    // arbitrary reshuffle.
    public IEnumerable<IQuickNavigationProvider> QuickNavigationProviders
    {
        get
        {
            var order = UserSettings.Load().QuickNavigationProviderOrder;
            return _quickNavigationProviders
                .Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QuickNavigationProvider, p.GetType().Name))
                .OrderBy(p =>
                {
                    var id = Helpers.PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(p), PluginComponentType.QuickNavigationProvider, p.GetType().Name);
                    var rank = order.IndexOf(id);
                    return rank >= 0 ? rank : int.MaxValue;
                });
        }
    }

    public IEnumerable<IQuickNavigationProvider> AllQuickNavigationProviders => _quickNavigationProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> InstantResultProviders
        => _instantResultProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.InstantProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> SearchableItemProviders
        => _searchableItemProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.SearchableItemProvider, p.GetType().Name));

    // Ordered per UserSettings.SidebarGroupOrder (position = priority, most-preferred first), one id per
    // PROVIDER rather than per group -- a provider whose id isn't listed there yet falls back to its own
    // SortOrder, so the built-in Type/Date/Size default ordering still holds until the user customizes it.
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> SidebarFilterProviders
    {
        get
        {
            var order = UserSettings.Load().SidebarGroupOrder;
            return _sidebarFilterProviders
                .OrderBy(p =>
                {
                    var id = Helpers.PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(p), PluginComponentType.FilterProvider, p.GetType().Name);
                    var rank = order.IndexOf(id);
                    return rank >= 0 ? rank : int.MaxValue;
                })
                .ThenBy(p => p.SortOrder)
                .Select(p => (PluginSdk.Abstractions.Plugins.ISidebarFilterProvider)new FilteredSidebarFilterProvider(p, ComponentFilter.GetDllName(p), this));
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
    public IEnumerable<IActivePathCollector> ActivePathCollectors => _pathCollectors;
    // Ordered per UserSettings.FilePreviewProviderOrder (position = priority, most-preferred first); a
    // provider whose id isn't listed there yet falls back to its own Priority (higher first), same
    // fallback shape SidebarFilterProviders/QuickNavigationProviders use for their own user-order lists.
    public IEnumerable<IFilePreviewProvider> FilePreviewProviders
    {
        get
        {
            var order = UserSettings.Load().FilePreviewProviderOrder;
            return _previewProviders
                .Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.FilePreviewProvider, p.GetType().Name))
                .OrderBy(p =>
                {
                    var id = Helpers.PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(p), PluginComponentType.FilePreviewProvider, p.GetType().Name);
                    var rank = order.IndexOf(id);
                    return rank >= 0 ? rank : int.MaxValue;
                })
                .ThenByDescending(p => p.Priority);
        }
    }

    // Ordered per UserSettings.ThumbnailProviderOrder (position = priority, most-preferred first); a
    // provider whose id isn't listed there yet falls back to its own Priority (higher first), same
    // fallback shape FilePreviewProviders above uses for its own user-order list.
    public IEnumerable<IThumbnailProvider> ThumbnailProviders
    {
        get
        {
            var order = UserSettings.Load().ThumbnailProviderOrder;
            return _thumbnailProviders
                .Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.ThumbnailProvider, p.GetType().Name))
                .OrderBy(p =>
                {
                    var id = Helpers.PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(p), PluginComponentType.ThumbnailProvider, p.GetType().Name);
                    var rank = order.IndexOf(id);
                    return rank >= 0 ? rank : int.MaxValue;
                })
                .ThenByDescending(p => p.Priority);
        }
    }

    public IEnumerable<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> QueryTokenProviders
        => _queryTokenProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QueryTokenProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider> StartupPanelTabProviders
        => _startupPanelTabProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.StartupPanelTabProvider, p.GetType().Name));

    public IEnumerable<IFilePreviewProvider> AllFilePreviewProviders => _previewProviders;
    public IEnumerable<IThumbnailProvider> AllThumbnailProviders => _thumbnailProviders;

    public IEnumerable<PluginActionRegistration> AllActions => _actions;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> AllDynamicActionProviders => _dynamicActionProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> AllInstantResultProviders => _instantResultProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> AllSearchableItemProviders => _searchableItemProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> AllSidebarFilterProviders => _sidebarFilterProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IResultColumnProvider> AllResultColumnProviders => _resultColumnProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ITranslationProvider> AllTranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IThemeProvider> AllThemeProviders => _themeProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> AllQueryTokenProviders => _queryTokenProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider> AllStartupPanelTabProviders => _startupPanelTabProviders;

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
        var single = new SimpleSearchResult[] { tempResult };

        foreach (var action in _actions)
        {
            if (action.Action.Keywords.Count == 0) continue;
            if (!action.Action.IsVisibleInSearch(single, windowType)) continue;
            if (!_filter.IsEnabled(ComponentFilter.GetDllName(action.Plugin), PluginComponentType.Action, action.Action.GetType().Name)) continue;
            if (!action.Action.CanExecute(single)) continue;

            var match = KeywordMatcher.TryMatchKeyword(query, action.Action.Keywords);
            if (match == null) continue;

            yield return new PluginSearchActionMatch(action, match.Value.Keyword, match.Value.ArgumentText);
        }
    }

    public bool TryExecuteSearchAction(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view, bool asAdmin = false)
        => PluginActionExecutor.TryExecute(result, view, asAdmin);

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
}
