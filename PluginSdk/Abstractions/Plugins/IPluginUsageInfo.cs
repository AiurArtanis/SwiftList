namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Provides optional, inline usage guidance for a plugin's card in Settings → Plugins.
/// </summary>
public interface IPluginUsageInfo
{
    string UsageInstructions { get; }
}
