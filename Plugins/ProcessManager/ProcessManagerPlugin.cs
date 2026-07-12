using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.ProcessManager;

public class ProcessManagerPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("ProcessManager_PluginName");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "ProcessManager_Config_TriggerKeywordLabel",
                DescriptionKey = "ProcessManager_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "ps",
                RequireNonEmpty = true
            }
        }
    };
}
