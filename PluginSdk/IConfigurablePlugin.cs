namespace SwiftList.PluginSdk;

public enum ConfigFieldType
{
    Boolean,
    Text,
    Integer,
    Choice,
    Array,
    Object
}

public class PluginConfigField
{
    public string Key { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public ConfigFieldType FieldType { get; set; }
    public object DefaultValue { get; set; } = null!;
    public List<string>? Choices { get; set; }
    public List<PluginConfigField>? SubFields { get; set; }
}

public class PluginConfigSchema
{
    public List<PluginConfigField> Fields { get; set; } = new();
}

public interface IConfigurablePlugin
{
    PluginConfigSchema GetConfigSchema();
}
