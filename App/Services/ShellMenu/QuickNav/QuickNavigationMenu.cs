using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using Imaging = System.Windows.Interop.Imaging;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Image = System.Windows.Controls.Image;
using Separator = System.Windows.Controls.Separator;
using Application = System.Windows.Application;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;

using SwiftList.App.Services.Plugin;
using SwiftList.App.Services.ShellIcons;
using SwiftList.App.Services.ShellMenu.ActionFlyout;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
using SwiftList.App.Services.ShellMenu.QuickNav.RightClickActions;
using SwiftList.App.Views.QuickSearchWindow.Helpers;
namespace SwiftList.App.Services.ShellMenu.QuickNav;

public static class QuickNavigationMenu
{
    public static bool IsShowingShellMenu { get; set; }

    // Bumped once per Show() call so a menu's own Closed handler can tell whether a NEWER Show() has
    // already started by the time it runs -- see that handler's own comment for the empty-submenu bug
    // this exists to fix.
    private static int _sessionGeneration;

    public static void Show(int mouseX, int mouseY)
    {
        var generation = ++_sessionGeneration;
        var tracker = InlineSearchManager.Instance.ExplorerTracker;

        // Captured now, before anything below (the helper window grabbing foreground, the popup sitting
        // open while the user browses it) has a chance to perturb ExplorerTracker's state -- see
        // QuickNavTriggerContext's own comment for why re-reading the tracker live at click time is not safe.
        var trigger = new QuickNavTriggerContext(
            DialogHwnd: tracker.IsExplorerOrDesktopActive && tracker.IsActiveWindowDialog ? tracker.ActiveHwnd : IntPtr.Zero,
            ActiveHwnd: tracker.ActiveHwnd,
            ActiveAdapter: tracker.ActiveInlineAdapter,
            IsDesktop: tracker.IsDesktop);

        var path = tracker.ActivePath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dummyResult = new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), IsDir = true };
        var contextMenu = new ContextMenu();
        contextMenu.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { contextMenu.IsOpen = false; e.Handled = true; } };

        foreach (var provider in PluginManager.Instance.QuickNavigationProviders)
        {
            if (!provider.CanProvide(dummyResult)) continue;
            provider.ClearSession();
            var providerItems = provider.GetMenuItems(dummyResult, IntPtr.Zero).ToList();
            if (providerItems.Count == 0) continue;

            // Shown even when this is the only active provider (by request) -- same "always label the
            // group, not just when there's more than one" convention the actions menu already follows.
            var headerAction = provider.HeaderAction;
            contextMenu.Items.Add(CreateGroupHeader(
                provider.GroupName,
                headerAction != null ? () => headerAction(dummyResult) : null,
                provider.HeaderActionTooltip,
                contextMenu));

            foreach (var item in providerItems)
                // Root entries are navigation categories (Favorites/History/configured folders/drives), so
                // don't attach the right-click action flyout here, and clicking/Enter must not execute or
                // navigate anywhere either -- only real files/folders in deeper levels do that.
                contextMenu.Items.Add(item.IsSeparator ? CreateSeparator() : CreateMenuItem(item, dummyResult, provider, contextMenu, trigger, enableRightClick: false, isRootItem: true));
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
        // useAltTapBypass: false -- this call is triggered by a mouse click the Hook's own mouse hook just
        // processed, which already satisfies SetForegroundWindow's foreground-lock check on its own. See
        // ForceForeground's own comment for why simulating Alt here caused this popup to self-deactivate.
        if (hwnd != IntPtr.Zero) QuickSearchWindowController.ForceForeground(hwnd, useAltTapBypass: false);

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

            // Every registered IQuickNavigationProvider/IDynamicActionProvider is a process-wide singleton
            // (PluginManager holds one shared instance, reused by every Show() call) -- ClearSession wipes
            // its handle->path lookup table, which the CURRENTLY OPEN menu's own submenu handles still
            // point into. Rapid re-triggering (e.g. several quick middle-clicks) can open a NEWER menu
            // before an OLDER one's Closed event has been delivered; when that stale event finally arrives
            // here and this ran unconditionally, it cleared the newer menu's still-live session data out
            // from under it, so hovering e.g. "This PC" resolved no path for its handle and rendered a
            // visibly empty submenu even though the menu itself was still open and otherwise fine. Only
            // clear when no NEWER Show() has started since this one did -- an older Closed event finding a
            // mismatch just skips cleanup this time, which is harmless (the next real Show() clears these
            // same lightweight dictionaries at its own start anyway, see the ClearSession call above).
            //
            // Release everything the menu pulled in so memory falls back immediately on close: dispose the
            // shell COM sessions (they own the native HMENU/HBITMAPs), drop the icon cache, then return
            // the freed pages to the OS. Deferred + off the UI thread so WPF first tears down the menu
            // visual tree (matching QuickSearch's hide path); otherwise the GC still sees it referenced.
            if (generation == _sessionGeneration)
            {
                foreach (var provider in PluginManager.Instance.QuickNavigationProviders) provider.ClearSession();
                foreach (var provider in PluginManager.Instance.DynamicActionProviders) provider.ClearSession();
                _ = Task.Delay(100).ContinueWith(_ =>
                {
                    try { ShellIconHelper.ClearCache(); } catch { }
                    try { Core.Win32Api.TrimWorkingSet(); } catch { }
                });
            }
        };

        contextMenu.Opened += (s, e) => contextMenu.Focus();
        contextMenu.IsOpen = true;
    }

    // Explicit SeparatorBrush reference (SetResourceReference, not a plain Style lookup) rather than a
    // bare `new Separator()`: this popup's items are built entirely in code with no local Style set, so
    // it was left depending on Menu.xaml's implicit TargetType="Separator" style resolving correctly for
    // an ad-hoc ContextMenu -- it visually came out a noticeably different, more saturated color than
    // the actions menu's own separator, which uses SeparatorBrush directly rather than through implicit
    // style matching. Forcing the same resource here, the same way ActionFlyout.cs already does for its
    // own code-built popup chrome (SetResourceReference, so it still follows live theme switching),
    // guarantees the two actually match instead of relying on two different resolution paths agreeing.
    internal static Separator CreateSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(Separator.BackgroundProperty, "SeparatorBrush");
        return separator;
    }

    // One per IQuickNavigationProvider contributing root-level items, labeling which provider they came
    // from -- same non-interactive, always-shown-even-for-a-single-group convention the actions menu's
    // own section headers use (ActionMenuItemTemplate's SectionHeaderVisibility block), via a dedicated
    // style (QuickNavGroupHeaderStyle in Menu.xaml) since this popup's items are plain MenuItems, not
    // ActionMenuItemTemplate-driven rows.
    internal static MenuItem CreateGroupHeader(string groupName, Action? headerAction, string? headerActionTooltip, ContextMenu contextMenu) =>
        CreateHeaderMenuItem(groupName, headerAction, headerActionTooltip, contextMenu);

    // Shared by the root-level provider group header (Show()) and any DynamicMenuItem a provider marks
    // IsHeader (CreateMenuItem below), so a submenu category header looks and behaves identically to
    // the root one. Header set to a plain string with no action, or a Grid (label + button) once one
    // is wired -- QuickNavGroupHeaderStyle's TemplateBinding only works for the former, hence the two
    // separate styles in Menu.xaml.
    private static MenuItem CreateHeaderMenuItem(string text, Action? headerAction, string? headerActionTooltip, ContextMenu contextMenu)
    {
        if (headerAction == null)
        {
            return new MenuItem
            {
                Header = text,
                Style = (Style)Application.Current.FindResource("QuickNavGroupHeaderStyle")
            };
        }

        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBlue"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        System.Windows.Controls.Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);

        var button = new System.Windows.Controls.Button
        {
            Content = "\uE710", // Segoe MDL2 Assets "Add" glyph
            Style = (Style)Application.Current.FindResource("QuickNavHeaderActionButtonStyle"),
            ToolTip = headerActionTooltip
        };
        System.Windows.Controls.Grid.SetColumn(button, 1);
        // Closes the menu and runs the action the same way triggerAction below does for any normal
        // leaf item's OnExecute -- including the PlacementTarget.Hide() call, which turned out to
        // matter here: without it, the prompt window PluginFieldPromptWindow.ShowInternal opens could
        // still pick the popup's own (by-then-closing) helper window as its Owner, and WPF closes a
        // window's owned windows right along with it -- the new prompt would flash open and
        // immediately close again. Deferred to Background so the menu is fully gone by the time the
        // action's own UI shows, matching every other item's click handling.
        button.Click += (s, e) =>
        {
            e.Handled = true;
            contextMenu.IsOpen = false;
            (contextMenu.PlacementTarget as Window)?.Hide();
            Application.Current.Dispatcher.BeginInvoke(headerAction, System.Windows.Threading.DispatcherPriority.Background);
        };
        grid.Children.Add(button);

        return new MenuItem
        {
            Header = grid,
            Focusable = false,
            // A click anywhere on this row (not just the button) would otherwise close the whole menu
            // per WPF's default leaf-MenuItem behavior, even though nothing was actually invoked --
            // this keeps the row itself inert; the button's own handler above closes the menu on its
            // own terms once headerAction has actually been dispatched.
            StaysOpenOnClick = true,
            Style = (Style)Application.Current.FindResource("QuickNavGroupHeaderWithActionStyle")
        };
    }

    internal static MenuItem CreateMenuItem(DynamicMenuItem item, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger, bool enableRightClick = true, bool isRootItem = false)
    {
        if (item.IsHeader)
        {
            return CreateHeaderMenuItem(item.Text, item.OnExecute, null, contextMenu);
        }

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
            menuItem.Items.Add(new MenuItem { Header = TranslationService.Get("QuickNav_Loading"), IsEnabled = false });
            menuItem.GotKeyboardFocus += (s, e) =>
            {
                QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger);
                Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (menuItem.IsKeyboardFocusWithin || menuItem.IsFocused) menuItem.IsSubmenuOpen = true; }));
            };
            menuItem.MouseEnter += (s, e) => QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger);
            menuItem.SubmenuOpened += (s, e) => { if (e.OriginalSource == menuItem) QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger); };
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
            // Root-level category entries (Favorites/History/configured folders) and any provider-marked
            // non-actionable node (e.g. an ini-defined submenu group with no real target of its own) are
            // pure navigation categories -- clicking/Enter must do nothing at all, not even close the menu.
            // Their contents are still reachable via submenu expansion (hover/keyboard-focus/right-arrow),
            // which is wired independently of this action below. Gated on HasSubMenu, not "isRootItem"
            // alone: a provider can legitimately put a genuinely actionable LEAF at the root too (e.g.
            // CustomCommandsQuickNavProvider's own commands with no configured submenu path), and those
            // must still fire on click/Enter same as any nested leaf does.
            if ((isRootItem && item.HasSubMenu) || !item.IsActionable) return;

            contextMenu.IsOpen = false;
            (contextMenu.PlacementTarget as Window)?.Hide();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (item.HasSubMenu)
                {
                    if (canNavigate) QuickNavigationNavigator.NavigateOrOpen(itemPath!, isDir: true, trigger);
                }
                else
                {
                    // Plugin-owned action: call OnExecute directly if set.
                    if (item.OnExecute != null)
                        item.OnExecute();
                    else if (!string.IsNullOrEmpty(itemPath))
                        QuickNavigationNavigator.NavigateOrOpen(itemPath, isDir: false, trigger);
                    else
                        provider.ExecuteCommand(result, item.CommandId, IntPtr.Zero);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };

        // Always intercept the right-click, even when the flyout itself is disabled (root items): WPF's
        // own MenuItem raises Click for a right mouse-button release too, not just left, so an
        // unhandled right-click here was falling through to the same triggerAction() a left-click uses
        // -- right-clicking a root-level leaf command silently ran it instead of doing nothing. Swallowing
        // it unconditionally (e.Handled = true) and only actually showing the flyout when enableRightClick
        // is set keeps root items truly inert on right-click, same as they already are on left-click.
        {
            Action triggerRightClickAction = () => PluginContextMenuHelper.Show(canNavigate, itemPath, item.HasSubMenu, menuItem, contextMenu);

            menuItem.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (FindVisualParent<MenuItem>(e.OriginalSource as DependencyObject) == menuItem)
                {
                    e.Handled = true;
                    if (enableRightClick) triggerRightClickAction();
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
            QuickNavigationMenuKeyHandler.HandlePreviewKeyDown(e, menuItem, item, contextMenu, itemPath, canNavigate, enableRightClick, triggerAction);

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

}
