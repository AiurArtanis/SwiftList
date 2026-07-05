using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
namespace SwiftList.App.Services;

/// <summary>
/// Reusable shell context menu presenter that drives the Actions list view
/// for any search window implementing ISearchWindow.
/// Supports dynamic actions and dynamic menu providers via plugins.
/// </summary>
public class ShellMenuPresenter : IDisposable
{
    private readonly ISearchWindow _view;
    private bool _isInActionsMode;
    private AppSearchResult? _activeResult;
    private IReadOnlyList<AppSearchResult> _activeResults = Array.Empty<AppSearchResult>();
    private readonly Stack<IntPtr> _menuStack = new();
    private readonly Stack<int> _menuSelectedIndexStack = new();
    private readonly Stack<string> _menuTitleStack = new();
    private readonly ShellMenuMouseInputHandler _mouseHandler;

    // Mappings to trace which provider owns which item/submenu at runtime

    private readonly Dictionary<uint, IDynamicActionProvider> _commandToProviderMap = new();
    private readonly Dictionary<IntPtr, IDynamicActionProvider> _subMenuToProviderMap = new();

    private string _savedSearchQuery = string.Empty;
    private List<ActionMenuItem> _currentRawItems = new();
    private int _actionsGeneration;

    public ShellMenuPresenter(ISearchWindow view)
    {
        _view = view;
        _mouseHandler = new ShellMenuMouseInputHandler(this, view);
        _view.SearchTextBox.TextChanged += (s, e) =>
        {
            if (_isInActionsMode)
            {
                ApplyFilter(_view.SearchTextBox.Text);
                _view.UpdateActionsLayout();
            }
        };
    }

    public bool IsInActionsMode => _isInActionsMode;
    public string SavedSearchQuery => _savedSearchQuery;

    public void EnterActionsMode(AppSearchResult result) => EnterActionsMode(new[] { result });

    /// <summary>
    /// Whether the actions menu is allowed to open for this selection right now. Scenarios that
    /// suppress the right-click menu (an adapter that opts out, apps outside the quick window,
    /// plugin/instant results, the "show more" row, an inline file dialog) also suppress action
    /// hotkeys, so callers gate on this.
    /// </summary>
    public bool CanShowActionsMenu(IReadOnlyList<AppSearchResult> selection)
    {
        var tracker = InlineSearchManager.Instance.ExplorerTracker;
        if (tracker.ActiveInlineAdapter != null && !tracker.ActiveInlineAdapter.CanEnterActionsMode(tracker.ActiveHwnd))
            return false;

        var items = selection?.Where(r => r != null && !r.IsSearchSectionHeader && !r.IsEmptyResult).ToList() ?? new List<AppSearchResult>();
        var result = items.Count > 0 ? items[0] : null;

        // Apps are only allowed into the actions list in the quick window; each action there still
        // self-guards via its own CanExecute (a real Start Menu shortcut has a file path actions can act
        // on, while a virtual shell:AppsFolder entry for a packaged app simply won't satisfy those checks).
        var appsAllowed = result == null || !result.IsApplication || GetWindowType() == SearchWindowType.Quick;

        return result != null && result.FullPath != "__SHOW_MORE__" && appsAllowed
            && !result.IsPluginSearchAction && !result.IsInstantResult && !IsInlineFileDialog()
            && !Helpers.FavoriteUrlHelper.IsWebUrl(result.FullPath);
    }

    public void EnterActionsMode(IReadOnlyList<AppSearchResult> selection)
    {
        if (!CanShowActionsMenu(selection))
            return;

        // Keep only real, actionable results; the first is the primary (used for the header).
        var items = selection?.Where(r => r != null && !r.IsSearchSectionHeader && !r.IsEmptyResult).ToList() ?? new List<AppSearchResult>();
        if (items.Count == 0)
            return;
        var result = items[0];

        _savedSearchQuery = _view.SearchTextBox.Text;
        _activeResults = items;
        _activeResult = result;
        _menuStack.Clear();
        _menuSelectedIndexStack.Clear();
        _menuTitleStack.Clear();
        _commandToProviderMap.Clear();
        _subMenuToProviderMap.Clear();

        foreach (var provider in PluginManager.Instance.DynamicProviders)
        {
            provider.ClearSession();
        }

        // 1. Show the built-in (static) actions immediately — this part is instant, so a slow shell
        //    context menu never delays the whole list appearing.
        _isInActionsMode = true;
        _view.IsInActionsMode = true;
        _view.GridSearchResults.Visibility = Visibility.Collapsed;
        _view.GridActions.Visibility = Visibility.Visible;
        _view.TxtActionsTarget.Text = Path.GetFileName(result.FullPath) + (items.Count > 1 ? $" (+{items.Count - 1})" : string.Empty);

        var generation = ++_actionsGeneration;
        _currentRawItems = ActionMenuBuilder.FinalizeItems(ActionMenuBuilder.BuildStatic(_activeResults, GetWindowType()));
        ApplyFilter(string.Empty);
        _view.SearchTextBox.Clear();
        _view.UpdateActionsLayout();

        // 2. Build the dynamic (potentially slow shell) group off the UI thread, capped at 2s. When it
        //    finishes in time, merge it in; on timeout, keep the built-in actions. A generation token
        //    stops a stale build from overwriting a newer/closed actions session.
        var selectionSnapshot = _activeResults;
        var windowType = GetWindowType();
        _ = Task.Run(() =>
        {
            var cmdMap = new Dictionary<uint, IDynamicActionProvider>();
            var subMap = new Dictionary<IntPtr, IDynamicActionProvider>();
            List<ActionMenuItem>? dynamicItems = null;
            try
            {
                var buildTask = Task.Run(
                    () => ActionMenuBuilder.BuildDynamic(selectionSnapshot, IntPtr.Zero, windowType, cmdMap, subMap));
                if (buildTask.Wait(2000))
                    dynamicItems = buildTask.Result;
                else
                    Core.Logger.Log("[ShellMenuPresenter] Dynamic actions build exceeded 2s; keeping built-in actions only.", Core.LogLevel.Warn);
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[ShellMenuPresenter] Dynamic actions build failed: {ex.Message}", Core.LogLevel.Error);
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isInActionsMode || generation != _actionsGeneration)
                    return; // user exited actions mode or opened a newer menu
                if (dynamicItems == null || dynamicItems.Count == 0)
                    return; // timeout / failure / nothing to add — built-in actions stay

                _commandToProviderMap.Clear();
                foreach (var kv in cmdMap) _commandToProviderMap[kv.Key] = kv.Value;
                _subMenuToProviderMap.Clear();
                foreach (var kv in subMap) _subMenuToProviderMap[kv.Key] = kv.Value;

                // Rebuild the static part on the UI thread (its vector icons may not be frozen) and
                // append the dynamic items; re-render while preserving the current filter/selection.
                var merged = ActionMenuBuilder.BuildStatic(_activeResults, GetWindowType());
                merged.AddRange(dynamicItems);
                _currentRawItems = ActionMenuBuilder.FinalizeItems(merged);
                ApplyFilter(_view.SearchTextBox.Text);
                _view.UpdateActionsLayout();
            }));
        });
    }

    private void LoadMenuItems(IntPtr hMenu)
    {
        if (_activeResult == null) return;
        // Update header text based on current menu level

        if (hMenu == IntPtr.Zero)
        {
            _view.TxtActionsTarget.Text = Path.GetFileName(_activeResult.FullPath);
        }

        else if (_menuTitleStack.Count > 0)
        {
            _view.TxtActionsTarget.Text = _menuTitleStack.Peek();
        }

        var finalItems = ActionMenuBuilder.Build(
            _activeResults,
            hMenu,
            GetWindowType(),
            _commandToProviderMap,
            _subMenuToProviderMap
        );
        _currentRawItems = finalItems;
        ApplyFilter(_view.SearchTextBox.Text);
        _view.UpdateActionsLayout();
    }

    private void ApplyFilter(string filter)
    {
        if (!_isInActionsMode) return;
        var cleanItems = ShellMenuFilter.Apply(_currentRawItems, filter);
        foreach (var item in cleanItems)
        {
            item.SearchQuery = filter;
        }
        _view.LstActions.ItemsSource = cleanItems;

        if (cleanItems.Count > 0)
        {
            var firstSelectable = cleanItems.FindIndex(i => !i.IsSeparator && !i.IsSectionHeader && !i.IsDisabled);
            _view.LstActions.SelectedIndex = firstSelectable >= 0 ? firstSelectable : 0;
            _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
        }
    }

    public void NavigateActionsList(int direction)
    {
        var count = _view.LstActions.Items.Count;
        if (count == 0) return;
        var index = _view.LstActions.SelectedIndex;
        var originalIndex = index;

        do
        {
            index = (index + direction + count) % count;
            if (index == originalIndex) break;
            if (_view.LstActions.Items[index] is ActionMenuItem item && !item.IsSeparator && !item.IsSectionHeader && !item.IsDisabled)
            {
                _view.LstActions.SelectedIndex = index;
                _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
                break;
            }

        } while (true);
    }

    public void EnterSubMenu()
    {
        if (_view.LstActions.SelectedItem is ActionMenuItem item && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            _menuStack.Push(item.SubMenuHandle);
            _menuSelectedIndexStack.Push(_view.LstActions.SelectedIndex);
            _menuTitleStack.Push(item.Text);
            LoadMenuItems(item.SubMenuHandle);
        }
    }

    public void GoBackMenuOrExit()
    {
        if (_menuStack.Count > 0)
        {
            _menuStack.Pop();
            if (_menuTitleStack.Count > 0) _menuTitleStack.Pop();
            var parentMenu = _menuStack.Count > 0 ? _menuStack.Peek() : IntPtr.Zero;
            LoadMenuItems(parentMenu);
            if (_menuSelectedIndexStack.Count > 0)
            {
                var prevIndex = _menuSelectedIndexStack.Pop();
                if (prevIndex >= 0 && prevIndex < _view.LstActions.Items.Count)
                {
                    _view.LstActions.SelectedIndex = prevIndex;
                    _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
                }
            }
        }
        else ExitActionsMode();
    }

    public void ExitActionsMode()
    {
        _activeResult = null;
        foreach (var provider in PluginManager.Instance.DynamicProviders)
        {
            provider.ClearSession();
        }

        _commandToProviderMap.Clear();
        _subMenuToProviderMap.Clear();
        _menuStack.Clear();
        _menuSelectedIndexStack.Clear();
        _menuTitleStack.Clear();
        _view.GridActions.Visibility = Visibility.Collapsed;
        _view.GridSearchResults.Visibility = Visibility.Visible;
        _view.UpdateActionsLayout();

        // Restore the saved query while IsInActionsMode is still true, so a host window's own
        // SearchTextBox.TextChanged handling (gated on IsInActionsMode, e.g. InlineSearchWindow's) treats
        // this restoration as a no-op instead of mistaking it for new typing -- which would otherwise wipe
        // the results selection and re-run the search, losing exactly which result/scroll position the
        // user was on before entering the actions menu.
        _view.SearchTextBox.Text = _savedSearchQuery;
        _view.SearchTextBox.SelectAll();

        _isInActionsMode = false;
        _view.IsInActionsMode = false;

        if (_view.LstResults.SelectedItem != null)
        {
            _view.LstResults.ScrollIntoView(_view.LstResults.SelectedItem);
        }
        _view.FocusSearch();
    }

    public void ExecuteSelectedAction()
    {
        if (_view.LstActions.SelectedItem is ActionMenuItem item)
        {
            if (item.IsSeparator || item.IsSectionHeader || item.IsDisabled) return;

            // 0. Direct delegate (e.g. CustomActions dynamic provider)
            if (item.OnExecute != null)
            {
                _view.HideWindow();
                item.OnExecute();
                return;
            }

            // 1. Handle custom SwiftList actions dynamically from PluginManager

            var registration = PluginManager.Instance.GetActionByRuntimeId(item.CommandId);
            if (registration != null)
            {
                var resultToExecute = _activeResult;
                if (resultToExecute != null)
                {
                    if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                    {
                        _view.HideWindow();
                    }

                    registration.Action.Execute(_activeResults, _view);
                }

                ExitActionsMode();
                return;
            }

            // 2. Handle submenus

            if (item.HasSubMenu)
            {
                EnterSubMenu();
                return;
            }

            // 3. Handle dynamic action provider executions

            if (_commandToProviderMap.TryGetValue(item.CommandId, out var provider))
            {
                var resultToExecute = _activeResult;
                if (resultToExecute != null)
                {
                    var hwnd = new WindowInteropHelper(_view as Window ?? System.Windows.Application.Current.MainWindow).Handle;
                    provider.ExecuteCommand(_activeResults, item.CommandId, hwnd);
                    if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                    {
                        _view.HideWindow();
                    }
                }

                ExitActionsMode();
            }
        }
    }

    public void HandleActionsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _mouseHandler.HandleActionsPreviewMouseLeftButtonUp(sender, e);

    public void Dispose() { foreach (var p in PluginManager.Instance.DynamicProviders) p.ClearSession(); }

    private SearchWindowType GetWindowType() =>
        _view.GetType().Name switch
        {
            "InlineSearchWindow" => SearchWindowType.Inline,
            "QuickSearchWindow" => SearchWindowType.Quick,
            _ => SearchWindowType.Main
        };

    private bool IsInlineFileDialog() => GetWindowType() == SearchWindowType.Inline && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog;
}
