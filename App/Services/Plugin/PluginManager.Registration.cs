using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.PluginSdk.Abstractions.Plugins.Preview;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowEffects;

namespace SwiftList.App.Services.Plugin;

public partial class PluginManager
{
    private readonly List<IQuickSearchWindowDragEffectProvider> _quickSearchWindowDragEffectProviders = new();

    void PluginRegistry.RegisterPlugin(PluginSdk.Abstractions.Plugins.IPlugin plugin) => RegisterPlugin(plugin);
    void PluginRegistry.AddInstantResultProvider(PluginSdk.Abstractions.Plugins.IInstantResultProvider p) => _instantResultProviders.Add(p);
    void PluginRegistry.AddSearchableItemProvider(PluginSdk.Abstractions.Plugins.ISearchableItemProvider p) => _searchableItemProviders.Add(p);
    void PluginRegistry.AddSidebarFilterProvider(PluginSdk.Abstractions.Plugins.ISidebarFilterProvider p) => _sidebarFilterProviders.Add(p);
    void PluginRegistry.AddResultColumnProvider(PluginSdk.Abstractions.Plugins.IResultColumnProvider p) => _resultColumnProviders.Add(p);
    void PluginRegistry.AddTranslationProvider(PluginSdk.Abstractions.Plugins.ITranslationProvider p) => _translationProviders.Add(p);
    void PluginRegistry.AddThemeProvider(PluginSdk.Abstractions.Plugins.IThemeProvider p) => _themeProviders.Add(p);
    void PluginRegistry.AddActivePathCollector(IActivePathCollector p)
    {
        _pathCollectors.Add(p);
        PluginSdk.Registries.ActivePathCollectorRegistry.Register(p);
    }
    void PluginRegistry.AddFilePreviewProvider(IFilePreviewProvider p) => _previewProviders.Add(p);
    void PluginRegistry.AddQuickNavigationProvider(IQuickNavigationProvider p) => _quickNavigationProviders.Add(p);
    void PluginRegistry.AddThumbnailProvider(IThumbnailProvider p) => _thumbnailProviders.Add(p);
    void PluginRegistry.AddQueryTokenProvider(PluginSdk.Abstractions.Plugins.IQueryTokenProvider p) => _queryTokenProviders.Add(p);
    void PluginRegistry.AddStartupPanelTabProvider(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider p) => _startupPanelTabProviders.Add(p);
    void PluginRegistry.AddQuickSearchWindowDragEffectProvider(IQuickSearchWindowDragEffectProvider p) => _quickSearchWindowDragEffectProviders.Add(p);

    public IEnumerable<IQuickSearchWindowDragEffectProvider> QuickSearchWindowDragEffectProviders
        => _quickSearchWindowDragEffectProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QuickSearchWindowDragEffectProvider, p.GetType().Name));

    public IEnumerable<IQuickSearchWindowDragEffectProvider> AllQuickSearchWindowDragEffectProviders => _quickSearchWindowDragEffectProviders;
}
