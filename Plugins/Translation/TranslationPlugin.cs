using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.Translation;

public class TranslationPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("Translation_PluginName");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "Translation_Config_TriggerKeywordLabel",
                DescriptionKey = "Translation_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "tr",
                RequireNonEmpty = true
            }
        }
    };
}
