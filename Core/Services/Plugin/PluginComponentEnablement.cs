using SwiftList.PluginSdk.Registries;

namespace SwiftList.Core.Services.Plugin;

// Component-enablement policy for the hook process: is a given plugin component (inline-search adapter,
// file-dialog adapter, active-path collector, alias provider) currently enabled per UserSettings'
// DisabledPluginComponents list. Kept separate from ServicePluginLoader, which only discovers/registers
// components -- deciding which loaded ones count as "active" is a different concern.
internal static class PluginComponentEnablement
{
    public static void WireFilterFuncs()
    {
        // Wire up FilterFuncs so the hook process respects enabled/disabled state.
        // The lambda reads UserSettings.Load() (cached) on every call, so after a
        // ReloadSettings command triggers UserSettings.ForceReload() the next adapter
        // lookup will automatically reflect the new disabled-components list.
        InlineSearchAdapterRegistry.FilterFunc = a => IsComponentEnabled(a);
        FileDialogAdapterRegistry.FilterFunc = a => IsComponentEnabled(a);
        ActivePathCollectorRegistry.FilterFunc = a => IsComponentEnabled(a);
    }

    public static bool IsComponentEnabled(object obj)
    {
        try
        {
            var dllName = Path.GetFileName(obj.GetType().Assembly.Location);
            var typeName = obj.GetType().Name;
            var settings = UserSettings.Load();

            // Match the same ID formats used by App's ComponentFilter / MakeId helper
            var idInlineSearch = $"{dllName}::InlineSearchAdapter::{typeName}";
            var idFileDialog = $"{dllName}::FileDialogAdapter::{typeName}";
            var idPathCollect = $"{dllName}::ActivePathCollector::{typeName}";
            var idAlias = $"{dllName}::AliasProvider::{typeName}";

            return !settings.DisabledPluginComponents.Contains(idInlineSearch, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idFileDialog, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idPathCollect, StringComparer.OrdinalIgnoreCase)
                && !settings.DisabledPluginComponents.Contains(idAlias, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}
