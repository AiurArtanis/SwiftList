using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SwiftList.PluginSdk;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using StackPanel = System.Windows.Controls.StackPanel;
using Border = System.Windows.Controls.Border;
using Separator = System.Windows.Controls.Separator;
using Application = System.Windows.Application;
using ItemsPanelTemplate = System.Windows.Controls.ItemsPanelTemplate;

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
                            var mItem = CreateActionMenuItem(subItem, dummyResult, provider, null, isFocusable: false);
                            
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

            var keyField = typeof(MenuItem).GetField("IsHighlightedPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var isHighlightedKey = keyField?.GetValue(null) as DependencyPropertyKey;
            
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

            rightClickMenu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((s, ev) =>
            {
                _currentRightClickPopup?.IsOpen = false;
                contextMenu.IsOpen = false;
                (contextMenu.PlacementTarget as Window)?.Hide();
            }));

            RoutedEventHandler? rootMenuClosedHandler = null;
            rootMenuClosedHandler = (s, ev) =>
            {
                _currentRightClickPopup?.IsOpen = false;
            };
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

    private static MenuItem CreateActionMenuItem(DynamicMenuItem item, ISearchResult result, IDynamicActionProvider provider, ContextMenu? contextMenu, bool isFocusable = true)
    {
        var menuItem = new MenuItem { Header = item.Text.Replace("&", ""), IsEnabled = !item.IsDisabled, Focusable = isFocusable && !item.IsDisabled };

        if (item.HBitmapItem != IntPtr.Zero)
        {
            try
            {
                menuItem.Icon = new System.Windows.Controls.Image
                {
                    Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        item.HBitmapItem,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions())
                };
            }
            catch { }
        }

        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            menuItem.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
            RoutedEventHandler ensureLoaded = null!;
            ensureLoaded = (s, e) =>
            {
                if (menuItem.Items.Count > 0 && (menuItem.Items[0] as MenuItem)?.Header?.ToString() == "Loading...")
                {
                    menuItem.Items.Clear();
                    foreach (var subItem in provider.GetMenuItems(result, item.SubMenuHandle))
                        menuItem.Items.Add(subItem.IsSeparator ? new Separator() : CreateActionMenuItem(subItem, result, provider, contextMenu, isFocusable));
                }
            };
            menuItem.SubmenuOpened += ensureLoaded;
        }
        else
        {
            menuItem.Click += (s, e) =>
            {
                if (e.Source == menuItem)
                {
                    _currentRightClickPopup?.IsOpen = false;
                    if (contextMenu != null)
                    {
                        contextMenu.IsOpen = false;
                        (contextMenu.PlacementTarget as Window)?.Hide();
                    }
                    var hwnd = Application.Current.MainWindow != null ? new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle : IntPtr.Zero;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => provider.ExecuteCommand(result, item.CommandId, hwnd)), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
        }

        return menuItem;
    }
}
