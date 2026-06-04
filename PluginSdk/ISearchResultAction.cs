using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// Represents an action that can be performed on a search result.
    /// </summary>
    public interface ISearchResultAction
    {
        /// <summary>
        /// A stable, locale-independent identifier for this action.
        /// Used to persist the enabled/disabled state across language changes.
        /// Defaults to the concrete type name — override only when you have multiple actions of the same class.
        /// </summary>
        string Id => GetType().Name;

        /// <summary>The group name this action belongs to (for visual categorisation).</summary>
        string GroupName { get; }

        /// <summary>The display name shown in the Actions menu.</summary>
        string DisplayName { get; }

        /// <summary>Keywords that expose this action as a search result instead of an action-menu item.</summary>
        IReadOnlyList<string> Keywords => Array.Empty<string>();

        /// <summary>Parameter names used for displaying the search-result form of this action.</summary>
        IReadOnlyList<string> Parameters => Array.Empty<string>();

        /// <summary>True when the search-result form of this action is only available in the inline window.</summary>
        bool InlineWindowOnly => false;

        /// <summary>The icon associated with this action.</summary>
        ImageSource? Icon { get; }

        /// <summary>Determines if this action is applicable to the given search result.</summary>
        bool CanExecute(ISearchResult result);

        /// <summary>Executes the action on the search result.</summary>
        void Execute(ISearchResult result, IPluginSearchWindow view);
    }
}
