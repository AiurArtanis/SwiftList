using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.Services;

internal static class ActionMenuBuilder
{
    public static List<ActionMenuItem> Build(
        AppSearchResult activeResult,
        IntPtr hMenu,
        SearchWindowType windowType,
        Dictionary<uint, IDynamicActionProvider> commandToProviderMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMenuToProviderMap)
    {
        if (activeResult == null)
            return new List<ActionMenuItem>();

        var uiItems = new List<ActionMenuItem>();
        var itemHeight = UiMetrics.ListItemHeight;

        var headerHeight = Math.Round(itemHeight * (UiMetrics.SearchSectionHeaderHeight / UiMetrics.SearchResultItemHeight));

        if (hMenu == IntPtr.Zero)
        {
            // Root menu: load static actions and dynamic providers
            commandToProviderMap.Clear();
            subMenuToProviderMap.Clear();

            // 1. Group and append static actions by GroupName
            var groupedActions = new Dictionary<string, List<PluginActionRegistration>>();
            foreach (var registration in PluginManager.Instance.Actions)
            {
                var action = registration.Action;
                if (!action.IsVisibleInMenu(activeResult, windowType))
                    continue;

                if (action.CanExecute(activeResult))
                {
                    var group = string.IsNullOrWhiteSpace(action.GroupName) ? TranslationManager.Instance["Action_BuiltinGroup"] : action.GroupName;
                    if (!groupedActions.TryGetValue(group, out var list))
                    {
                        list = new List<PluginActionRegistration>();
                        groupedActions[group] = list;
                    }
                    list.Add(registration);
                }
            }

            foreach (var kvp in groupedActions)
            {
                uiItems.Add(new ActionMenuItem
                {
                    IsSectionHeader = true,
                    SectionTitle = kvp.Key,
                    ItemHeight = headerHeight
                });

                foreach (var registration in kvp.Value)
                {
                    var action = registration.Action;
                    uiItems.Add(new ActionMenuItem
                    {
                        Text = action.DisplayName,
                        CommandId = registration.RuntimeActionId,
                        Icon = action.Icon,
                        ItemHeight = itemHeight,
                        ShortcutHint = action.Hotkey
                    });
                }
            }

            // 2. Load dynamic provider actions (e.g. Shell Context Menu)
            foreach (var provider in PluginManager.Instance.DynamicProviders)
            {
                if (provider.Keywords.Count > 0)
                    continue;

                if (!provider.IsVisibleInMenu(activeResult, windowType))
                    continue;

                if (provider.CanProvide(activeResult))
                {
                    var dynamicItems = provider.GetMenuItems(activeResult, IntPtr.Zero);
                    var dynamicItemsList = new List<DynamicMenuItem>(dynamicItems);

                    if (dynamicItemsList.Count > 0)
                    {
                        var group = string.IsNullOrWhiteSpace(provider.GroupName) ? TranslationManager.Instance["Action_BuiltinGroup"] : provider.GroupName;
                        uiItems.Add(new ActionMenuItem
                        {
                            IsSectionHeader = true,
                            SectionTitle = group,
                            ItemHeight = headerHeight
                        });

                        foreach (var item in dynamicItemsList)
                        {
                            if (string.IsNullOrWhiteSpace(item.Text) && !item.IsSeparator)
                                continue;

                            var iconSource = GetIconFromHBitmap(item.HBitmapItem);

                            uiItems.Add(new ActionMenuItem
                            {
                                Text = (item.Text ?? "").Replace("&", ""),
                                CommandId = item.CommandId,
                                IsSeparator = item.IsSeparator,
                                HasSubMenu = item.HasSubMenu,
                                SubMenuHandle = item.SubMenuHandle,
                                IsDisabled = item.IsDisabled,
                                Icon = iconSource,
                                ItemHeight = item.IsSeparator ? Math.Round(itemHeight * 0.3) : itemHeight
                            });

                            if (!item.IsSeparator && item.CommandId != 0)
                            {
                                commandToProviderMap[item.CommandId] = provider;
                            }
                            if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
                            {
                                subMenuToProviderMap[item.SubMenuHandle] = provider;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Submenu navigation: lookup the owning dynamic provider
            if (subMenuToProviderMap.TryGetValue(hMenu, out var provider))
            {
                var dynamicItems = provider.GetMenuItems(activeResult, hMenu);
                foreach (var item in dynamicItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Text) && !item.IsSeparator)
                        continue;

                    var iconSource = GetIconFromHBitmap(item.HBitmapItem);

                    uiItems.Add(new ActionMenuItem
                    {
                        Text = (item.Text ?? "").Replace("&", ""),
                        CommandId = item.CommandId,
                        IsSeparator = item.IsSeparator,
                        HasSubMenu = item.HasSubMenu,
                        SubMenuHandle = item.SubMenuHandle,
                        IsDisabled = item.IsDisabled,
                        Icon = iconSource,
                        ItemHeight = item.IsSeparator ? Math.Round(itemHeight * 0.3) : itemHeight
                    });

                    if (!item.IsSeparator && item.CommandId != 0)
                    {
                        commandToProviderMap[item.CommandId] = provider;
                    }
                    if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
                    {
                        subMenuToProviderMap[item.SubMenuHandle] = provider;
                    }
                }
            }
        }

        // Deduplicate items based on text
        var uniqueItems = new List<ActionMenuItem>();
        foreach (var item in uiItems)
        {
            if (item.IsSeparator || item.IsSectionHeader)
            {
                uniqueItems.Add(item);
                continue;
            }

            var existing = uniqueItems.Find(x => !x.IsSeparator && !x.IsSectionHeader && x.Text.Equals(item.Text, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (item.HasSubMenu && !existing.HasSubMenu)
                {
                    uniqueItems.Remove(existing);
                    uniqueItems.Add(item);
                }
            }
            else
            {
                uniqueItems.Add(item);
            }
        }

        // Clean up separators
        var finalItems = new List<ActionMenuItem>();
        for (var i = 0; i < uniqueItems.Count; i++)
        {
            var current = uniqueItems[i];
            if (current.IsSeparator)
            {
                if (finalItems.Count == 0) continue;
                if (finalItems[finalItems.Count - 1].IsSeparator || finalItems[finalItems.Count - 1].IsSectionHeader) continue;
                if (i == uniqueItems.Count - 1) continue;
            }
            finalItems.Add(current);
        }

        return finalItems;
    }

    private static System.Windows.Media.ImageSource? GetIconFromHBitmap(IntPtr hBitmap)
    {
        if (hBitmap == IntPtr.Zero) return null;

        var val = hBitmap.ToInt64();
        if (val == -1 || val == 2 || val == 3 || val == 4 || val == 5) return null;

        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ActionMenuBuilder] GetIconFromHBitmap failed: {ex.Message}", Core.LogLevel.Error);
            return null;
        }
    }
}
