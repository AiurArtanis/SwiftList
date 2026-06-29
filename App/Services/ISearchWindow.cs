using System.Windows;
using System.Windows.Controls;
using ListBox = System.Windows.Controls.ListBox;
using SwiftList.PluginSdk.Abstractions;

namespace SwiftList.App.Services;

/// <summary>
/// Shared interface between QuickSearchWindow and InlineSearchWindow
/// to decouple and share the ShellMenuPresenter context menu controller.
/// </summary>
public interface ISearchWindow : IPluginSearchWindow
{
    UIElement ResultsPanel { get; }
    ListBox LstResults { get; }
    Grid GridSearchResults { get; }
    Grid GridActions { get; }
    TextBlock TxtActionsTarget { get; }
    ListBox LstActions { get; }
    string SearchText { get; }
    void UpdateActionsLayout();
    void FocusSearch();
}
