using SwiftList.PluginSdk;

namespace SwiftList.App.Services.PluginManagerCore
{
    /// <summary>
    /// Callback interface used by <see cref="PluginLoader"/> to register discovered
    /// plugin components back into the owning <see cref="Services.PluginManager"/>.
    /// </summary>
    internal interface PluginRegistry
    {
        void RegisterPlugin(IActionPlugin plugin);
        void AddInstantResultProvider(IInstantResultProvider provider);
        void AddSidebarFilterProvider(ISidebarFilterProvider provider);
        void AddResultColumnProvider(IResultColumnProvider provider);
        void AddTranslationProvider(ITranslationProvider provider);
        void AddThemeProvider(IThemeProvider provider);
        void AddActivePathCollector(IActivePathCollector provider);
        void AddFilePreviewProvider(IFilePreviewProvider provider);
    }
}
