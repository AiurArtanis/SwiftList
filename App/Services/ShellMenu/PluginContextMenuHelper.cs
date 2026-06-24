using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MenuItem = System.Windows.Controls.MenuItem; using ContextMenu = System.Windows.Controls.ContextMenu;
using StackPanel = System.Windows.Controls.StackPanel; using Border = System.Windows.Controls.Border;
using Separator = System.Windows.Controls.Separator; using Application = System.Windows.Application;
using ItemsPanelTemplate = System.Windows.Controls.ItemsPanelTemplate; using KeyEventHandler = System.Windows.Input.KeyEventHandler;

namespace SwiftList.App.Services;

public static class PluginContextMenuHelper
{
    private static Popup? _currentRightClickPopup;

    public static void Show(bool canNavigate, string? itemPath, bool hasSubMenu, MenuItem menuItem, ContextMenu contextMenu)
    {
        if (!canNavigate || string.IsNullOrEmpty(itemPath)) return;

        QuickNavigationMenu.IsShowingShellMenu = true;

        var dummyResult = new AppSearchResult { FullPath = itemPath, Name = Path.GetFileName(itemPath), IsDir = hasSubMenu || Directory.Exists(itemPath) };
        var rightClickMenu = new System.Windows.Controls.Menu
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Focusable = false
        };
        
        var template = new ItemsPanelTemplate();
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(System.Windows.Controls.Panel.IsItemsHostProperty, true);
        template.VisualTree = factory;
        rightClickMenu.ItemsPanel = template;

        var menuItems = new List<MenuItem>();
        var highlightedIndex = -1;

        var addedTexts = new HashSet<string>();
        foreach (var provider in PluginManager.Instance.DynamicProviders)
        {
            if (provider.Keywords.Count > 0) continue;
            if (provider.CanProvide(dummyResult))
            {
                provider.ClearSession();
                var dynamicItems = provider.GetMenuItems(dummyResult, IntPtr.Zero);
                foreach (var subItem in dynamicItems)
                {
                    if (subItem.IsSeparator)
                    {
                        rightClickMenu.Items.Add(new Separator());
                    }
                    else
                    {
                        if (addedTexts.Add(subItem.Text))
                        {
                            var mItem = PluginContextMenuBuilder.CreateActionMenuItem(subItem, dummyResult, provider, null, isFocusable: false);
                            menuItems.Add(mItem);
                            
                            mItem.MouseEnter += (s, ev) =>
                            {
                                foreach (var child in rightClickMenu.Items)
                                {
                                    if (child is MenuItem childItem && childItem != mItem && childItem.IsSubmenuOpen)
                                    {
                                        childItem.IsSubmenuOpen = false;
                                    }
                                }
                                if (mItem.HasItems && !mItem.IsSubmenuOpen)
                                {
                                    mItem.IsSubmenuOpen = true;
                                }
                            };
                            
                            rightClickMenu.Items.Add(mItem);
                        }
                    }
                }
            }
        }

        if (rightClickMenu.Items.Count > 0)
        {
            var border = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("MenuBackground"),
                BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("MenuBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Child = rightClickMenu
            };

            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12, ShadowDepth = 2, Direction = 270,
                Color = (System.Windows.Media.Color)Application.Current.FindResource("ShadowColor"),
                Opacity = 0.08
            };

            _currentRightClickPopup = new Popup
            {
                PlacementTarget = menuItem,
                Placement = PlacementMode.MousePoint,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = border
            };

            var keyField = typeof(MenuItem).GetField("IsHighlightedPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var isHighlightedKey = keyField?.GetValue(null) as DependencyPropertyKey;

            Action<int> updateHighlight = (newIdx) =>
            {
                if (isHighlightedKey == null) return;
                if (highlightedIndex >= 0 && highlightedIndex < menuItems.Count)
                    menuItems[highlightedIndex].SetValue(isHighlightedKey, false);
                highlightedIndex = newIdx;
                if (highlightedIndex >= 0 && highlightedIndex < menuItems.Count)
                {
                    menuItems[highlightedIndex].SetValue(isHighlightedKey, true);
                    menuItems[highlightedIndex].BringIntoView();
                }
            };

            for (var i = 0; i < menuItems.Count; i++)
            {
                var idx = i;
                menuItems[i].MouseEnter += (s, ev) => updateHighlight(idx);
            }
            rightClickMenu.MouseLeave += (s, ev) => updateHighlight(-1);

            MouseButtonEventHandler? mouseDownHandler = null;
            mouseDownHandler = (s, ev) =>
            {
                if (_currentRightClickPopup != null && _currentRightClickPopup.IsOpen)
                {
                    if (ev.OriginalSource is DependencyObject clickedElement)
                    {
                        var inPopup = QuickNavigationMenu.FindVisualParent<Border>(clickedElement) == border;
                        if (!inPopup)
                        {
                            _currentRightClickPopup.IsOpen = false;
                        }
                    }
                }
            };
            contextMenu.AddHandler(UIElement.PreviewMouseDownEvent, mouseDownHandler, true);

            menuItem.Focus();
            if (isHighlightedKey != null)
            {
                menuItem.SetValue(isHighlightedKey, true);
            }

            System.Windows.Input.MouseEventHandler? mouseLeaveHandler = null;
            mouseLeaveHandler = (s, ev) =>
            {
                if (_currentRightClickPopup != null && _currentRightClickPopup.IsOpen)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                    {
                        if (_currentRightClickPopup != null && _currentRightClickPopup.IsOpen && isHighlightedKey != null)
                            menuItem.SetValue(isHighlightedKey, true);
                    }));
                }
            };
            menuItem.MouseLeave += mouseLeaveHandler;

            System.Windows.Input.MouseEventHandler? mouseMoveHandler = null;
            mouseMoveHandler = (s, ev) =>
            {
                if (_currentRightClickPopup != null && _currentRightClickPopup.IsOpen)
                {
                    ev.Handled = true;
                }
            };
            contextMenu.AddHandler(UIElement.PreviewMouseMoveEvent, mouseMoveHandler, true);

            KeyEventHandler? keyHandler = (s, ev) =>
            {
                if (_currentRightClickPopup == null || !_currentRightClickPopup.IsOpen || isHighlightedKey == null) return;
                var state = PluginContextMenuBuilder.GetActiveMenuState(rightClickMenu, isHighlightedKey);
                if (state.items.Count == 0) return;

                Action<int> updateStateHighlight = (newIdx) =>
                {
                    if (state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count)
                        state.items[state.highlightedIndex].SetValue(isHighlightedKey, false);
                    if (newIdx >= 0 && newIdx < state.items.Count)
                    {
                        state.items[newIdx].SetValue(isHighlightedKey, true);
                        state.items[newIdx].BringIntoView();
                    }
                };

                if (ev.Key == Key.Down)
                {
                    ev.Handled = true;
                    updateStateHighlight((state.highlightedIndex + 1) % state.items.Count);
                }
                else if (ev.Key == Key.Up)
                {
                    ev.Handled = true;
                    updateStateHighlight((state.highlightedIndex - 1 + state.items.Count) % state.items.Count);
                }
                else if (ev.Key == Key.Right)
                {
                    var activeItem = (state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count) ? state.items[state.highlightedIndex] : null;
                    if (activeItem != null && activeItem.HasItems)
                    {
                        ev.Handled = true;
                        activeItem.IsSubmenuOpen = true;
                        var subItems = activeItem.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled).ToList();
                        if (subItems.Count > 0) subItems[0].SetValue(isHighlightedKey, true);
                    }
                }
                else if (ev.Key == Key.Left)
                {
                    if (state.parent is MenuItem parentMenuItem)
                    {
                        ev.Handled = true;
                        parentMenuItem.IsSubmenuOpen = false;
                    }
                }
                else if (ev.Key == Key.Escape)
                {
                    ev.Handled = true;
                    _currentRightClickPopup.IsOpen = false;
                }
                else if ((ev.Key == Key.Enter || ev.Key == Key.Space) && state.highlightedIndex >= 0)
                {
                    var activeItem = state.items[state.highlightedIndex];
                    ev.Handled = true;
                    if (activeItem.HasItems)
                    {
                        activeItem.IsSubmenuOpen = true;
                        var subItems = activeItem.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled).ToList();
                        if (subItems.Count > 0) subItems[0].SetValue(isHighlightedKey, true);
                    }
                    else
                    {
                        activeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    }
                }
            };
            contextMenu.AddHandler(UIElement.PreviewKeyDownEvent, keyHandler, true);

            rightClickMenu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((s, ev) =>
            {
                _currentRightClickPopup?.IsOpen = false;
                contextMenu.IsOpen = false;
                (contextMenu.PlacementTarget as Window)?.Hide();
            }));

            RoutedEventHandler? rootMenuClosedHandler = null;
            rootMenuClosedHandler = (s, ev) => _currentRightClickPopup?.IsOpen = false;
            contextMenu.Closed += rootMenuClosedHandler;

            _currentRightClickPopup.Closed += (s, ev) =>
            {
                if (mouseDownHandler != null)
                    contextMenu.RemoveHandler(UIElement.PreviewMouseDownEvent, mouseDownHandler);
                if (mouseMoveHandler != null)
                    contextMenu.RemoveHandler(UIElement.PreviewMouseMoveEvent, mouseMoveHandler);
                if (mouseLeaveHandler != null)
                    menuItem.MouseLeave -= mouseLeaveHandler;
                if (rootMenuClosedHandler != null)
                    contextMenu.Closed -= rootMenuClosedHandler;
                if (keyHandler != null)
                    contextMenu.RemoveHandler(UIElement.PreviewKeyDownEvent, keyHandler);

                if (isHighlightedKey != null)
                {
                    menuItem.SetValue(isHighlightedKey, false);
                    if (menuItem.IsMouseOver) menuItem.SetValue(isHighlightedKey, true);
                }

                QuickNavigationMenu.IsShowingShellMenu = false;
                if (contextMenu.PlacementTarget is Window win && contextMenu.IsOpen)
                {
                    win.Activate();
                    contextMenu.Focus();
                }
                if (_currentRightClickPopup == s) _currentRightClickPopup = null;
            };

            _currentRightClickPopup.IsOpen = true;
        }
        else
        {
            QuickNavigationMenu.IsShowingShellMenu = false;
        }
    }

    internal static void ClosePopup() => _currentRightClickPopup?.IsOpen = false;
}
