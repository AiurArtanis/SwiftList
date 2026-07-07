namespace SwiftList.PluginSdk.Services;

/// <summary>
/// A decoupled service to provide read access to plugin-specific settings from the host application.
/// </summary>
public static class PluginSettingsService
{
    /// <summary>
    /// Delegate function set by the host application to retrieve plugin settings.
    /// Parameters: (pluginId, settingKey, defaultValue)
    /// </summary>
    public static Func<string, string, object?, object?>? GetSettingFunc { get; set; }

    /// <summary>
    /// Retrieves a setting value for a specific plugin.
    /// </summary>
    public static T GetSetting<T>(string pluginId, string key, T defaultValue)
    {
        if (GetSettingFunc == null) return defaultValue;
        try
        {
            var val = GetSettingFunc(pluginId, key, defaultValue);
            if (val is T typedVal) return typedVal;
            if (val != null)
            {
                if (val is System.Text.Json.JsonElement element)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText())!;
                }

                try
                {
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(val);
                    return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
                }
            }
        }
        catch
        {
            // Fallback to default
        }
        return defaultValue;
    }
}
