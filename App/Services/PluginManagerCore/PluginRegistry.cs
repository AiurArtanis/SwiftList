using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.Services.PluginManagerCore;

/// <summary>
/// Callback interface used by <see cref="PluginLoader"/> to register discovered
/// plugin components back into the owning <see cref="PluginManager"/>.
/// </summary>
internal interface PluginRegistry
{
    void RegisterPlugin(IAction plugin);
    void AddInstantResultProvider(IInstantResultProvider provider);
    void AddSearchableItemProvider(ISearchableItemProvider provider);
    void AddSidebarFilterProvider(ISidebarFilterProvider provider);
    void AddResultColumnProvider(IResultColumnProvider provider);
    void AddTranslationProvider(ITranslationProvider provider);
    void AddThemeProvider(IThemeProvider provider);
    void AddActivePathCollector(IActivePathCollector provider);
    void AddFilePreviewProvider(IFilePreviewProvider provider);
    void AddQuickNavigationProvider(IQuickNavigationProvider provider);
}
