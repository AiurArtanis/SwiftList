namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents the base interface for all plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>The name of the plugin.</summary>
    string Name { get; }
}
