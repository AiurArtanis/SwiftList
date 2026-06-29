namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a plugin that provides one or more search result actions.
/// </summary>
public interface IAction
{
    /// <summary>The name of the plugin.</summary>
    string Name { get; }


    /// <summary>Returns the actions provided by this plugin.</summary>
    IEnumerable<ISearchResultAction> GetActions();

    /// <summary>Returns the dynamic action providers (e.g. Shell Context Menu) provided by this plugin.</summary>
    IEnumerable<IDynamicActionProvider> GetDynamicProviders();
}