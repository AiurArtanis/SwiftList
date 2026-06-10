using System.Collections.Generic;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// Represents a provider that can process the search query in real-time
    /// and output instant results directly to the search result list.
    /// </summary>
    public interface IInstantResultProvider
    {
        /// <summary>
        /// A stable, locale-independent identifier for this provider.
        /// Used to persist the enabled/disabled state across language changes.
        /// Defaults to the concrete type name.
        /// </summary>
        string Id => GetType().Name;

        /// <summary>The name of the provider.</summary>
        string Name { get; }

        /// <summary>The version of the provider.</summary>
        string Version => GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        /// <summary>
        /// Processes the query and returns instant result items.
        /// Returns null or empty if this query is not handled by the provider.
        /// </summary>
        IEnumerable<InstantResultItem> GetInstantResults(string query);
    }

    /// <summary>
    /// Represents an individual instant result item to be displayed in the results list.
    /// </summary>
    public class InstantResultItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional vector path string for a custom icon.</summary>
        public string? IconData { get; set; }

        /// <summary>Optional custom hex color (e.g. #3399FF) for the vector icon.</summary>
        public string? IconColor { get; set; }

        /// <summary>
        /// Action to perform when double clicked or entered.
        /// "Copy" (copy ActionArgument to clipboard),
        /// "Execute" (run command/script),
        /// "None"
        /// </summary>
        public string ActionType { get; set; } = "Copy";

        /// <summary>The argument associated with the action (e.g. the text to copy).</summary>
        public string ActionArgument { get; set; } = string.Empty;

        /// <summary>Optional custom text to fill the search box with when Tab is pressed.</summary>
        public string? TabCompletion { get; set; }
    }
}
