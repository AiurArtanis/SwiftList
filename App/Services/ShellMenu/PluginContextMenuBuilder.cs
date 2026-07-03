using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Separator = System.Windows.Controls.Separator;
using Image = System.Windows.Controls.Image;
using Application = System.Windows.Application;
using ItemsControl = System.Windows.Controls.ItemsControl;

namespace SwiftList.App.Services;

public static class PluginContextMenuBuilder
{
    public static MenuItem CreateActionMenuItem(DynamicMenuItem item, ISearchResult result, IDynamicActionProvider provider, ContextMenu? contextMenu, bool isFocusable = true)
    {
        var menuItem = new MenuItem { Header = item.Text.Replace("&", ""), IsEnabled = !item.IsDisabled, Focusable = isFocusable && !item.IsDisabled };

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

        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            menuItem.Items.Add(new MenuItem { Header = "Loading...", IsEnabled = false });
            RoutedEventHandler ensureLoaded = null!;
            ensureLoaded = (s, e) =>
            {
                if (menuItem.Items.Count > 0 && (menuItem.Items[0] as MenuItem)?.Header?.ToString() == "Loading...")
                {
                    menuItem.Items.Clear();
                    foreach (var subItem in provider.GetMenuItems(new[] { result }, item.SubMenuHandle))
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
                    PluginContextMenuHelper.ClosePopup();
                    if (contextMenu != null)
                    {
                        contextMenu.IsOpen = false;
                        (contextMenu.PlacementTarget as Window)?.Hide();
                    }
                    var hwnd = Application.Current.MainWindow != null ? new WindowInteropHelper(Application.Current.MainWindow).Handle : IntPtr.Zero;
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => provider.ExecuteCommand(new[] { result }, item.CommandId, hwnd)), System.Windows.Threading.DispatcherPriority.Background);
                }
            };
        }

        return menuItem;
    }

    public static (DependencyObject parent, List<MenuItem> items, int highlightedIndex) GetActiveMenuState(DependencyObject root, DependencyPropertyKey? key)
    {
        var current = root;
        while (true)
        {
            var items = new List<MenuItem>();
            if (current is ItemsControl ic)
                items.AddRange(ic.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled));
            var openSub = items.FirstOrDefault(mi => mi.IsSubmenuOpen);
            if (openSub != null) { current = openSub; continue; }
            var idx = -1;
            for (var i = 0; i < items.Count; i++)
                if (key != null && (bool)items[i].GetValue(key.DependencyProperty)) { idx = i; break; }
            return (current, items, idx);
        }
    }
}
