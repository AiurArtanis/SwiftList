using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.Plugins.DirectoryOpus;

public class DirectoryOpusPlugin : IPlugin, IConfigurable, ITranslationProvider
{
    public string Name => "Directory Opus";

    public IReadOnlyList<string> SupportedCultures => new[] { "zh-CN", "en-US" };

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>
            {
                { "Plugins_DirectoryOpus_EnableInlineSearch", "启用内联搜索" },
                { "Plugins_DirectoryOpus_EnableInlineSearchDesc", "允许在 Directory Opus 窗口中双击或按快捷键弹出内置搜索栏" }
            };
        }
        return new Dictionary<string, string>
        {
            { "Plugins_DirectoryOpus_EnableInlineSearch", "Enable Inline Search" },
            { "Plugins_DirectoryOpus_EnableInlineSearchDesc", "Allow double-clicking or pressing hotkeys in Directory Opus to summon inline search" }
        };
    }

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "EnableInlineSearch",
                LabelKey = "Plugins_DirectoryOpus_EnableInlineSearch",
                DescriptionKey = "Plugins_DirectoryOpus_EnableInlineSearchDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            }
        }
    };
}
