using SwiftList.Core;

namespace SwiftList.App.Services.Plugin;

public partial class PluginManager
{
    internal object? GetPluginSetting(string pluginId, string key, object? defaultValue)
    {
        var settings = UserSettings.Load();
        if (settings.PluginSettings.TryGetValue(pluginId, out var pluginDict) && pluginDict.ContainsKey(key))
        {
            return settings.GetPluginSetting(pluginId, key, defaultValue);
        }
        if (_pluginSchemaDefaults.TryGetValue(pluginId, out var fieldDefaults) && fieldDefaults.TryGetValue(key, out var schemaDefault))
        {
            return schemaDefault;
        }
        return defaultValue;
    }

    internal void SetPluginSetting(string pluginId, string key, object? value)
    {
        var settings = UserSettings.Load();
        object? normalized = value == null ? null : System.Text.Json.JsonSerializer.SerializeToElement(value);
        settings.SetPluginSetting(pluginId, key, normalized);
        settings.Save();
    }
}
