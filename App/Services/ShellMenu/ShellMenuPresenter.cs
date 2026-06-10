using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SwiftList.Core;
using SwiftList.PluginSdk;
namespace SwiftList.App.Services
{
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
        private readonly Stack<IntPtr> _menuStack = new();
        private readonly Stack<int> _menuSelectedIndexStack = new();
        private readonly Stack<string> _menuTitleStack = new();
        private readonly ShellMenuMouseInputHandler _mouseHandler;

        // Mappings to trace which provider owns which item/submenu at runtime

        private readonly Dictionary<uint, IDynamicActionProvider> _commandToProviderMap = new();
        private readonly Dictionary<IntPtr, IDynamicActionProvider> _subMenuToProviderMap = new();

        public ShellMenuPresenter(ISearchWindow view)
        {
            _view = view;
            _mouseHandler = new ShellMenuMouseInputHandler(this, view);
        }

        public bool IsInActionsMode => _isInActionsMode;

        public void EnterActionsMode(AppSearchResult result)
        {
            var tracker = InlineSearchManager.Instance.ExplorerTracker;
            if (tracker.ActiveInlineAdapter != null && !tracker.ActiveInlineAdapter.CanEnterActionsMode(tracker.ActiveHwnd))
            {
                return;
            }

            if (result == null
                || result.FullPath == "__SHOW_MORE__"

                || result.IsEmptyResult

                || result.IsApplication

                || result.IsPluginSearchAction

                || result.IsSearchSectionHeader

                || result.IsInstantResult

                || IsInlineFileDialog())
            {
                return;
            }

            _activeResult = result;
            _menuStack.Clear();
            _menuSelectedIndexStack.Clear();
            _menuTitleStack.Clear();
            _commandToProviderMap.Clear();
            _subMenuToProviderMap.Clear();

            // Clear previous sessions in all registered dynamic action providers

            foreach (var provider in PluginManager.Instance.DynamicProviders)
            {
                provider.ClearSession();
            }

            _isInActionsMode = true;

            // Transition UI

            _view.GridSearchResults.Visibility = Visibility.Collapsed;
            _view.GridActions.Visibility = Visibility.Visible;
            _view.TxtActionsTarget.Text = Path.GetFileName(result.FullPath);
            LoadMenuItems(IntPtr.Zero);
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

                _activeResult,
                hMenu,
                IsInlineWindow(),
                _commandToProviderMap,
                _subMenuToProviderMap

            );
            _view.LstActions.ItemsSource = finalItems;
            _view.UpdateActionsLayout();
            if (finalItems.Count > 0)
            {
                int firstSelectable = finalItems.FindIndex(i => !i.IsSeparator && !i.IsSectionHeader && !i.IsDisabled);
                _view.LstActions.SelectedIndex = firstSelectable >= 0 ? firstSelectable : 0;
                _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
            }
        }

        public void NavigateActionsList(int direction)
        {
            int count = _view.LstActions.Items.Count;
            if (count == 0) return;
            int index = _view.LstActions.SelectedIndex;
            int originalIndex = index;

            do
            {
                index = (index + direction + count) % count;
                if (index == originalIndex) break;
                var item = _view.LstActions.Items[index] as ActionMenuItem;
                if (item != null && !item.IsSeparator && !item.IsSectionHeader && !item.IsDisabled)
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
                if (_menuTitleStack.Count > 0)
                {
                    _menuTitleStack.Pop();
                }

                IntPtr parentMenu = _menuStack.Count > 0 ? _menuStack.Peek() : IntPtr.Zero;
                LoadMenuItems(parentMenu);
                if (_menuSelectedIndexStack.Count > 0)
                {
                    int prevIndex = _menuSelectedIndexStack.Pop();
                    if (prevIndex >= 0 && prevIndex < _view.LstActions.Items.Count)
                    {
                        _view.LstActions.SelectedIndex = prevIndex;
                        _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
                    }
                }
            }

            else
            {
                ExitActionsMode();
            }
        }

        public void ExitActionsMode()
        {
            _isInActionsMode = false;
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
            if (_view.LstResults.SelectedItem != null)
            {
                _view.LstResults.ScrollIntoView(_view.LstResults.SelectedItem);
            }
        }

        public void ExecuteSelectedAction()
        {
            if (_view.LstActions.SelectedItem is ActionMenuItem item)
            {
                if (item.IsSeparator || item.IsSectionHeader || item.IsDisabled) return;
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

                        registration.Action.Execute(resultToExecute, _view);
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
                        provider.ExecuteCommand(resultToExecute, item.CommandId, hwnd);
                        if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                        {
                            _view.HideWindow();
                        }
                    }

                    ExitActionsMode();
                }
            }
        }

        public void HandleActionsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)

            => _mouseHandler.HandleActionsPreviewMouseLeftButtonUp(sender, e);

        public void Dispose()
        {
            foreach (var provider in PluginManager.Instance.DynamicProviders)
            {
                provider.ClearSession();
            }
        }

        private bool IsInlineWindow()
        {
            return _view.GetType().Name.Equals("InlineSearchWindow", StringComparison.Ordinal);
        }

        private bool IsInlineFileDialog()
        {
            return IsInlineWindow() && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog;
        }
    }
}
