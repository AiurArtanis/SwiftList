using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.BrowserData;

// One user-configured browser profile directory to index (bookmarks + history). Added manually in
// plugin settings rather than auto-discovered -- profile locations/naming vary too much across browser
// versions, channels (stable/beta/dev), and multi-profile setups to guess reliably, and auto-enumerating
// would silently start indexing browsing history the user never opted into.
public class BrowserProfileConfig
{
    public string Name { get; set; } = string.Empty;
    // Key/property named "Icon" specifically (not "IconData") -- the settings UI's icon-preview swatch
    // (Templates.xaml's IsIconField trigger) only activates for a Text field whose key is exactly "Icon".
    public string Icon { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class BrowserDataPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("BrowserData_PluginName");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "BrowserData_Config_TriggerKeywordLabel",
                DescriptionKey = "BrowserData_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "bm",
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "Profiles",
                LabelKey = "BrowserData_Config_ProfilesLabel",
                DescriptionKey = "BrowserData_Config_ProfilesDesc",
                FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(),
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "Name",
                        LabelKey = "BrowserData_Config_NameLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Icon",
                        LabelKey = "BrowserData_Config_IconDataLabel",
                        DescriptionKey = "BrowserData_Config_IconDataDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Path",
                        LabelKey = "BrowserData_Config_PathLabel",
                        DescriptionKey = "BrowserData_Config_PathDesc",
                        FieldType = ConfigFieldType.FolderPath,
                        DefaultValue = ""
                    }
                }
            }
        }
    };
}
