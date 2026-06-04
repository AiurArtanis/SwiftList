using System.Collections.Generic;

namespace SwiftList.App.Services
{
    /// <summary>
    /// Represents a plugin that provides one or more search result actions.
    /// </summary>
    public interface IActionPlugin
    {
        /// <summary>The name of the plugin.</summary>
        string Name { get; }

        /// <summary>The version of the plugin.</summary>
        string Version => GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        /// <summary>Returns the actions provided by this plugin.</summary>
        IEnumerable<ISearchResultAction> GetActions();

        /// <summary>Returns the dynamic action providers (e.g. Shell Context Menu) provided by this plugin.</summary>
        IEnumerable<IDynamicActionProvider> GetDynamicProviders();
    }
}
