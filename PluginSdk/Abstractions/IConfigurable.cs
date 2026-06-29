namespace SwiftList.PluginSdk.Abstractions;

public enum ConfigFieldType
{
    Boolean,
    Text,
    Integer,
    Choice,
    Array,
    Object,
    Group,
    StringList
}

public class PluginConfigField
{
    public string Key { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
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

public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
