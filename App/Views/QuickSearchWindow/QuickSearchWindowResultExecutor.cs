using System.Windows;
using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search;
using SwiftList.Core;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace SwiftList.App.Views.QuickSearchWindow;

// Executes a selected/clicked search result from the results list -- split out of QuickSearchWindow
// itself to keep that file under the line-count limit.
public class QuickSearchWindowResultExecutor
{
    private readonly SwiftList.App.QuickSearchWindow _window;

    public QuickSearchWindowResultExecutor(SwiftList.App.QuickSearchWindow window) => _window = window;

    public void HandlePreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = SwiftList.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var asAdmin = Keyboard.Modifiers == ModifierKeys.Control;
            Execute(result, asAdmin);
        }
    }

    public void HandlePreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var item = SwiftList.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            _window.LstResults.SelectedItem = result;
            _window.MenuPresenter?.EnterActionsMode(result);
        }
    }

    public void Execute(AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsSearchSectionHeader)
            return;
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(_window.TxtSearch.Text, result.FullPath, SearchResultHelper.HistoryKindOf(result));
        }

        if (result.IsPluginSearchAction)
        {
            _window.HideWindow();
            PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin);
            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin))
        {
            _window.HideWindow();
            return;
        }

        var currentQuery = _window.TxtSearch.Text;
        if (result.FullPath == "__SHOW_MORE__")
        {
            // The full window has no concept of a per-type trigger, so one is stripped before it lands there.
            var carriedQuery = SearchResultTypePriority.StripLeadingTrigger(currentQuery);
            _window.HideWindowNoRestore();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
        }
        else
        {
            _window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, _window.HideWindow);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, _window.HideWindow);
        }
    }
}
