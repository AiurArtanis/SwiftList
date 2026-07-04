using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using Imaging = System.Windows.Interop.Imaging;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Image = System.Windows.Controls.Image;
using Separator = System.Windows.Controls.Separator;
using Application = System.Windows.Application;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;

namespace SwiftList.App.Services;

public static class QuickNavigationMenu
{
    public static bool IsShowingShellMenu { get; set; }

    public static void Show(int mouseX, int mouseY)
    {
        var path = InlineSearchManager.Instance.ExplorerTracker.ActivePath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dummyResult = new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), IsDir = true };
        var contextMenu = new ContextMenu();
        contextMenu.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { contextMenu.IsOpen = false; e.Handled = true; } };

        foreach (var provider in PluginManager.Instance.QuickNavigationProviders)
        {
            if (!provider.CanProvide(dummyResult)) continue;
            provider.ClearSession();
            foreach (var item in provider.GetMenuItems(dummyResult, IntPtr.Zero))
                // Root entries are navigation categories (Favorites/History/drives), so don't attach the
                // right-click action flyout here; only real files/folders in deeper levels get it.
                contextMenu.Items.Add(item.IsSeparator ? new Separator() : CreateMenuItem(item, dummyResult, provider, contextMenu, enableRightClick: false));
        }

        if (contextMenu.Items.Count == 0) return;

        double dpiScaleX = 1.0, dpiScaleY = 1.0;
        var src = Application.Current.MainWindow != null ? PresentationSource.FromVisual(Application.Current.MainWindow) : null;
        if (src?.CompositionTarget != null)
        {
            dpiScaleX = src.CompositionTarget.TransformFromDevice.M11;
            dpiScaleY = src.CompositionTarget.TransformFromDevice.M22;
        }

        var helperWin = new MenuHelperWindow(mouseX * dpiScaleX, mouseY * dpiScaleY);
        helperWin.Deactivated += (s, e) => { if (!IsShowingShellMenu) contextMenu.IsOpen = false; };
        helperWin.Show();
        helperWin.Activate();

        var hwnd = new WindowInteropHelper(helperWin).Handle;
        if (hwnd != IntPtr.Zero) Views.QuickSearchWindow.QuickSearchWindowController.ForceForeground(hwnd);

        contextMenu.PlacementTarget = helperWin;
        contextMenu.Placement = PlacementMode.AbsolutePoint;
        contextMenu.HorizontalOffset = mouseX * dpiScaleX;
        contextMenu.VerticalOffset = mouseY * dpiScaleY;

        Action<int, int> clickOutsideHandler = (x, y) =>
        {
            if (!Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y))
                Application.Current.Dispatcher.BeginInvoke(() => contextMenu.IsOpen = false);
        };

        if (App.HookClient != null)
        {
            App.HookClient.OnMouseClick += clickOutsideHandler;
            App.HookClient.OnMouseDoubleClick += clickOutsideHandler;
            App.HookClient.OnMouseMiddleClick += clickOutsideHandler;
        }

        contextMenu.Closed += (s, e) =>
        {
            if (App.HookClient != null)
            {
                App.HookClient.OnMouseClick -= clickOutsideHandler;
                App.HookClient.OnMouseDoubleClick -= clickOutsideHandler;
                App.HookClient.OnMouseMiddleClick -= clickOutsideHandler;
            }
            helperWin.Close();

            // Release everything the menu pulled in so memory falls back immediately on close: dispose the
            // shell COM sessions (they own the native HMENU/HBITMAPs), drop the icon cache, then return
            // the freed pages to the OS. Deferred + off the UI thread so WPF first tears down the menu
            // visual tree (matching QuickSearch's hide path); otherwise the GC still sees it referenced.
            foreach (var provider in PluginManager.Instance.QuickNavigationProviders) provider.ClearSession();
            foreach (var provider in PluginManager.Instance.DynamicProviders) provider.ClearSession();
            _ = Task.Delay(100).ContinueWith(_ =>
            {
                try { ShellIconHelper.ClearCache(); } catch { }
                try { Core.Win32Api.TrimWorkingSet(); } catch { }
            });
        };

        contextMenu.Opened += (s, e) => contextMenu.Focus();
        contextMenu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(DynamicMenuItem item, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, bool enableRightClick = true)
    {
        var menuItem = new MenuItem { Header = item.Text, IsEnabled = !item.IsDisabled, Focusable = !item.IsDisabled };

        if (item.HBitmapItem != IntPtr.Zero)
        {
            try
            {
                menuItem.Icon = new Image
                {
                    Source = Imaging.CreateBitmapSourceFromHBitmap(
                        item.HBitmapItem,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions())
                };
            }
            catch { }
        }

        string? itemPath = null;
        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            itemPath = QuickNavigationPathResolver.TryResolveSubMenuPath(provider, item.SubMenuHandle);
            menuItem.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
            menuItem.GotKeyboardFocus += (s, e) =>
            {
                EnsureSubItemsLoaded(menuItem, result, item, provider, contextMenu);
                Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (menuItem.IsKeyboardFocusWithin || menuItem.IsFocused) menuItem.IsSubmenuOpen = true; }));
            };
            menuItem.MouseEnter += (s, e) => EnsureSubItemsLoaded(menuItem, result, item, provider, contextMenu);
            menuItem.SubmenuOpened += (s, e) => { if (e.OriginalSource == menuItem) EnsureSubItemsLoaded(menuItem, result, item, provider, contextMenu); };
        }
        else
        {
            itemPath = QuickNavigationPathResolver.TryResolveCommandPath(provider, item.CommandId);
        }

        if (item.HBitmapItem == IntPtr.Zero && !string.IsNullOrEmpty(itemPath) && item.OnExecute == null)
        {
            if (Helpers.FavoriteUrlHelper.IsWebUrl(itemPath))
            {
                menuItem.Icon = new Image { Source = Helpers.FavoriteUrlHelper.Icon };
            }
            else
            {
                var isDir = item.HasSubMenu;
                var cached = ShellIconHelper.GetIconFromCacheOnly(itemPath, isDir, out var needsLoad);
                if (cached != null) menuItem.Icon = new Image { Source = cached };
                if (needsLoad)
                {
                    Task.Run(() =>
                    {
                        var icon = ShellIconHelper.GetIconForPath(itemPath, isDir);
                        if (icon != null) Application.Current.Dispatcher.BeginInvoke(() => menuItem.Icon = new Image { Source = icon });
                    });
                }
            }
        }

        var canNavigate = !string.IsNullOrEmpty(itemPath) &&
                          (itemPath.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) ||
                           itemPath.StartsWith("::", StringComparison.OrdinalIgnoreCase) ||
                           Directory.Exists(itemPath) ||
                           File.Exists(itemPath));

        Action triggerAction = () =>
        {
            contextMenu.IsOpen = false;
            (contextMenu.PlacementTarget as Window)?.Hide();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (item.HasSubMenu)
                {
                    if (canNavigate) QuickNavigationNavigator.NavigateOrOpen(itemPath!);
                }
                else
                {
                    // Plugin-owned action: call OnExecute directly if set.
                    if (item.OnExecute != null)
                        item.OnExecute();
                    else if (!string.IsNullOrEmpty(itemPath))
                        QuickNavigationNavigator.NavigateOrOpen(itemPath);
                    else
                        provider.ExecuteCommand(result, item.CommandId, IntPtr.Zero);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };

        if (enableRightClick)
        {
            Action triggerRightClickAction = () => PluginContextMenuHelper.Show(canNavigate, itemPath, item.HasSubMenu, menuItem, contextMenu);

            menuItem.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (FindVisualParent<MenuItem>(e.OriginalSource as DependencyObject) == menuItem)
                {
                    e.Handled = true;
                    triggerRightClickAction();
                }
            }), handledEventsToo: true);
        }

        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            menuItem.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (FindVisualParent<MenuItem>(e.OriginalSource as DependencyObject) == menuItem)
                {
                    e.Handled = true;
                    triggerAction();
                }
            }), handledEventsToo: true);
        }
        else
        {
            menuItem.Click += (s, e) => { if (e.Source == menuItem) triggerAction(); };
        }

        menuItem.PreviewKeyDown += (s, e) =>
        {
            // Action hotkeys (Ctrl+C, Ctrl+Enter, ...) fire directly on the highlighted item without
            // opening its action menu — like the full window's result list. Gated to real file/folder
            // items (same places the action menu is allowed), so nav categories don't respond.
            if (menuItem.IsFocused && enableRightClick && canNavigate && !string.IsNullOrEmpty(itemPath)
                && System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
            {
                var hotkeySelection = new[]
                {
                    new AppSearchResult
                    {
                        FullPath = itemPath!,
                        Name = Path.GetFileName(itemPath),
                        IsDir = item.HasSubMenu || Directory.Exists(itemPath),
                        ContextDirectory = Directory.Exists(itemPath) ? itemPath! : (Path.GetDirectoryName(itemPath) ?? string.Empty)
                    }
                };
                var shim = new QuickNavShimView(() =>
                {
                    contextMenu.IsOpen = false;
                    (contextMenu.PlacementTarget as Window)?.Hide();
                });
                if (Helpers.HotkeyActionTrigger.TryExecute(e, hotkeySelection, shim, SearchWindowType.Main, hideOnRun: true))
                {
                    e.Handled = true;
                    return;
                }
            }

            if ((e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return) && menuItem.IsFocused)
            {
                e.Handled = true;
                triggerAction();
                return;
            }
            if (menuItem.IsFocused)
            {
                if (e.Key == System.Windows.Input.Key.Down)
                {
                    if (NavigateToSibling(menuItem, forward: true)) { menuItem.IsSubmenuOpen = false; e.Handled = true; }
                }
                else if (e.Key == System.Windows.Input.Key.Up)
                {
                    if (NavigateToSibling(menuItem, forward: false)) { menuItem.IsSubmenuOpen = false; e.Handled = true; }
                }
                else if (e.Key == System.Windows.Input.Key.Right && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
                {
                    if (menuItem.Items.OfType<MenuItem>().All(c => !c.IsEnabled)) e.Handled = true;
                    else
                    {
                        var firstChild = menuItem.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable);
                        if (firstChild != null) { firstChild.Focus(); e.Handled = true; }
                    }
                }
            }
            else if (menuItem.IsSubmenuOpen && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
            {
                if (System.Windows.Input.Keyboard.FocusedElement is MenuItem focused && menuItem.Items.Contains(focused))
                {
                    if (e.Key == System.Windows.Input.Key.Left)
                    {
                        menuItem.IsSubmenuOpen = false;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => menuItem.Focus()));
                        e.Handled = true;
                    }
                    else if (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up)
                    {
                        var items = menuItem.Items.OfType<MenuItem>().Where(i => i.IsEnabled && i.Focusable).ToList();
                        var index = items.IndexOf(focused);
                        if (index != -1 && items.Count > 0)
                        {
                            var nextIndex = e.Key == System.Windows.Input.Key.Down ? (index + 1) % items.Count : (index - 1 + items.Count) % items.Count;
                            items[nextIndex].Focus();
                            e.Handled = true;
                        }
                    }
                }
                else if ((e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down) && menuItem.Items.OfType<MenuItem>().All(c => !c.IsEnabled))
                {
                    menuItem.IsSubmenuOpen = false;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => menuItem.Focus()));
                    e.Handled = true;
                }
            }
        };

        return menuItem;
    }

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T p) return p;
            child = child is FrameworkContentElement fce ? fce.Parent : System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static void EnsureSubItemsLoaded(MenuItem menuItem, ISearchResult result, DynamicMenuItem item, IQuickNavigationProvider provider, ContextMenu contextMenu)
    {
        if (menuItem.Items.Count > 0 && (menuItem.Items[0] as MenuItem)?.Header?.ToString() != "Loading...") return;
        menuItem.Items.Clear();
        foreach (var subItem in provider.GetMenuItems(result, item.SubMenuHandle))
            menuItem.Items.Add(CreateMenuItem(subItem, result, provider, contextMenu));
    }

    private static bool NavigateToSibling(MenuItem currentItem, bool forward)
    {
        var parent = System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(currentItem);
        var items = parent?.Items.OfType<MenuItem>().Where(i => i.IsEnabled && i.Focusable).ToList();
        var idx = items?.IndexOf(currentItem) ?? -1;
        if (idx == -1 || items == null || items.Count == 0) return false;
        var nextIdx = (idx + (forward ? 1 : -1) + items.Count) % items.Count;
        items[nextIdx].Focus();
        return true;
    }
}
