using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.WindowGuides;

public sealed class WindowGuidesPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("WindowGuides_PluginName");
    public string Description => TranslationService.Get("WindowGuides_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields = new List<PluginConfigField>
        {
            Slider("GuideOpacity", "WindowGuides_Config_GuideOpacity", 50, 0, 100),
            Slider("GuideThickness", "WindowGuides_Config_GuideThickness", 1, 1, 8),
            Slider("OutlineOpacity", "WindowGuides_Config_OutlineOpacity", 50, 0, 100),
            Slider("OutlineThickness", "WindowGuides_Config_OutlineThickness", 2, 1, 8)
        }
    };

    private static PluginConfigField Slider(string key, string translationKey, int defaultValue, int minimum, int maximum) => new()
    {
        Key = key,
        LabelKey = $"{translationKey}Label",
        DescriptionKey = $"{translationKey}Desc",
        FieldType = ConfigFieldType.Slider,
        DefaultValue = defaultValue,
        Minimum = minimum,
        Maximum = maximum,
        Step = 1
    };
}
