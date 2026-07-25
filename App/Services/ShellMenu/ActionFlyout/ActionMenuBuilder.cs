using System.Text.RegularExpressions;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;

using SwiftList.App.Services.Plugin;
using SwiftList.App.Helpers;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
namespace SwiftList.App.Services.ShellMenu.ActionFlyout;

internal static class ActionMenuBuilder
{
    // Windows shell menu text carries an access-key mnemonic SwiftList's own hotkey system never wires
    // up (this menu is keyboard-navigated, not accelerator-key-driven) -- stripping only the leading
    // "&" left the parenthesized letter itself behind for the common localized layout ("Word(&D)" ->
    // "Word(D)"), showing a dead "(D)" hint that can never actually fire.
    private static readonly Regex MnemonicPattern = new(@"\(&.\)", RegexOptions.Compiled);

    private static string CleanMenuText(string? text) =>
        MnemonicPattern.Replace(text ?? "", "").Replace("&", "");

    // Stable, non-localized id for a static action-group section, persisted in
    // UserSettings.ActionMenuGroupOrder -- groupKey is the already-resolved display string BuildStatic
    // groups by (either action.GroupName or the built-in-label fallback), so it's compared against that
    // same resolved label rather than re-deriving from raw action data.
    internal static string BuildStaticGroupId(string groupKey, string builtinLabel) =>
        groupKey == builtinLabel ? "__builtin__" : $"static::{groupKey}";

    // Stable, non-localized id for a dynamic-provider section header, matching the same
    // "{dllName}::{ComponentType}::{name}" scheme DisabledPluginComponents already uses for this
    // provider (PluginLoaderHelper.MakeId), so no new id format is introduced.
    internal static string BuildDynamicGroupId(IDynamicActionProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.DynamicActionProvider, provider.GetType().Name);

    public static List<ActionMenuItem> Build(
        IReadOnlyList<AppSearchResult> selection,
        IntPtr hMenu,
        SearchWindowType windowType,
        Dictionary<uint, IDynamicActionProvider> commandToProviderMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMenuToProviderMap)
    {
        if (selection == null || selection.Count == 0)
            return new List<ActionMenuItem>();

        List<ActionMenuItem> uiItems;
        if (hMenu == IntPtr.Zero)
        {
            // Root menu: built-in actions + dynamic providers.
            commandToProviderMap.Clear();
            subMenuToProviderMap.Clear();
            uiItems = BuildStatic(selection, windowType);
            uiItems.AddRange(BuildDynamic(selection, IntPtr.Zero, windowType, commandToProviderMap, subMenuToProviderMap));
        }
        else
        {
            // Submenu navigation is dynamic-only.
            uiItems = BuildDynamic(selection, hMenu, windowType, commandToProviderMap, subMenuToProviderMap);
        }

        return FinalizeItems(uiItems);
    }

    // Builds ONLY the built-in (static) action items — the fast part of the menu. The presenter shows
    // these immediately (on the UI thread, where the vector icons are created) and appends the
    // potentially-slow dynamic (shell) group asynchronously.
    public static List<ActionMenuItem> BuildStatic(IReadOnlyList<AppSearchResult> selection, SearchWindowType windowType)
    {
        var uiItems = new List<ActionMenuItem>();
        if (selection == null || selection.Count == 0)
            return uiItems;

        var itemHeight = Math.Round(UiMetrics.ListItemHeight * UiMetrics.ActionMenuCompactRowScale);
        // Unified to the exact same height a normal row gets (was a shorter, independently-derived
        // value) -- height-sum calculations elsewhere that assume a uniform row size can't drift from
        // what a header row actually renders at if the two are never allowed to be different numbers
        // in the first place.
        var headerHeight = itemHeight;

        var builtinLabel = TranslationManager.Instance["Action_BuiltinGroup"];
        var groupedActions = new Dictionary<string, List<PluginActionRegistration>>();
        foreach (var registration in PluginManager.Instance.Actions)
        {
            var action = registration.Action;
            if (!action.IsVisibleInMenu(selection, windowType))
                continue;

            if (action.CanExecute(selection))
            {
                var group = string.IsNullOrWhiteSpace(action.GroupName) ? builtinLabel : action.GroupName;
                if (!groupedActions.TryGetValue(group, out var list))
                {
                    list = new List<PluginActionRegistration>();
                    groupedActions[group] = list;
                }
                list.Add(registration);
            }
        }

        var pluginActionHotkeys = Core.UserSettings.Load().Hotkeys.PluginActionHotkeys;

        foreach (var kvp in groupedActions)
        {
            uiItems.Add(new ActionMenuItem
            {
                IsSectionHeader = true,
                SectionTitle = kvp.Key,
                SectionGroupId = BuildStaticGroupId(kvp.Key, builtinLabel),
                ItemHeight = headerHeight
            });

            foreach (var registration in kvp.Value)
            {
                var action = registration.Action;
                var effectiveHotkey = action.Hotkey;
                var pluginId = System.IO.Path.GetFileNameWithoutExtension(PluginManagerCore.ComponentFilter.GetDllName(registration.Plugin));
                if (pluginActionHotkeys.TryGetValue(pluginId, out var overrides)
                    && overrides.TryGetValue(action.GetType().Name, out var overrideHotkey))
                {
                    effectiveHotkey = overrideHotkey;
                }

                uiItems.Add(new ActionMenuItem
                {
                    Text = action.DisplayName,
                    CommandId = registration.RuntimeActionId,
                    Icon = action.Icon,
                    ItemHeight = itemHeight,
                    ShortcutHint = effectiveHotkey
                });
            }
        }
        return uiItems;
    }

    // Builds the dynamic-provider items (shell context menu, etc). This can be slow (loading shell
    // extensions), so the presenter runs it OFF the UI thread and appends the result to the already
    // shown static items. Its icons come from HBITMAPs and are frozen, so they are safe cross-thread.
    public static List<ActionMenuItem> BuildDynamic(
        IReadOnlyList<AppSearchResult> selection,
        IntPtr hMenu,
        SearchWindowType windowType,
        Dictionary<uint, IDynamicActionProvider> commandToProviderMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMenuToProviderMap)
    {
        var uiItems = new List<ActionMenuItem>();
        if (selection == null || selection.Count == 0)
            return uiItems;

        var itemHeight = Math.Round(UiMetrics.ListItemHeight * UiMetrics.ActionMenuCompactRowScale);
        // Unified to the exact same height a normal row gets (was a shorter, independently-derived
        // value) -- height-sum calculations elsewhere that assume a uniform row size can't drift from
        // what a header row actually renders at if the two are never allowed to be different numbers
        // in the first place.
        var headerHeight = itemHeight;

        if (hMenu == IntPtr.Zero)
        {
            // Root: every dynamic provider (e.g. shell context menu), sorted by Priority ascending
            // (lower values first, matching IDynamicActionProvider.Priority's own doc comment -- this
            // was previously OrderByDescending, silently contradicting that doc).
            foreach (var provider in PluginManager.Instance.DynamicActionProviders.OrderBy(p => p.Priority))
            {
                if (provider.Keywords.Count > 0)
                    continue;

                if (!provider.IsVisibleInMenu(selection, windowType))
                    continue;

                if (provider.CanProvide(selection))
                {
                    var dynamicItems = provider.GetMenuItems(selection, IntPtr.Zero);
                    var dynamicItemsList = new List<DynamicMenuItem>(dynamicItems);

                    if (dynamicItemsList.Count > 0)
                    {
                        var group = string.IsNullOrWhiteSpace(provider.GroupName) ? TranslationManager.Instance["Action_BuiltinGroup"] : provider.GroupName;
                        uiItems.Add(new ActionMenuItem
                        {
                            IsSectionHeader = true,
                            SectionTitle = group,
                            SectionGroupId = BuildDynamicGroupId(provider),
                            ItemHeight = headerHeight
                        });

                        foreach (var item in dynamicItemsList)
                        {
                            if (string.IsNullOrWhiteSpace(item.Text) && !item.IsSeparator)
                                continue;

                            var iconSource = GetIconFromHBitmap(item.HBitmapItem);

                            uiItems.Add(new ActionMenuItem
                            {
                                Text = CleanMenuText(item.Text),
                                CommandId = item.CommandId,
                                IsSeparator = item.IsSeparator,
                                HasSubMenu = item.HasSubMenu,
                                SubMenuHandle = item.SubMenuHandle,
                                IsDisabled = item.IsDisabled,
                                Icon = iconSource,
                                OnExecute = item.OnExecute,
                                ShortcutHint = item.ShortcutHint ?? string.Empty,
                                ItemHeight = item.IsSeparator ? Math.Round(itemHeight * UiMetrics.ActionMenuSeparatorRowScale) : itemHeight
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
            // Submenu navigation: look up the owning dynamic provider.
            if (subMenuToProviderMap.TryGetValue(hMenu, out var provider))
            {
                var dynamicItems = provider.GetMenuItems(selection, hMenu);
                foreach (var item in dynamicItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Text) && !item.IsSeparator)
                        continue;

                    var iconSource = GetIconFromHBitmap(item.HBitmapItem);

                    uiItems.Add(new ActionMenuItem
                    {
                        Text = CleanMenuText(item.Text),
                        CommandId = item.CommandId,
                        IsSeparator = item.IsSeparator,
                        HasSubMenu = item.HasSubMenu,
                        SubMenuHandle = item.SubMenuHandle,
                        IsDisabled = item.IsDisabled,
                        Icon = iconSource,
                        ItemHeight = item.IsSeparator ? Math.Round(itemHeight * UiMetrics.ActionMenuSeparatorRowScale) : itemHeight
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
        return uiItems;
    }

    // Dedupes by text and tidies separators. Runs on the merged (static + dynamic) list.
    public static List<ActionMenuItem> FinalizeItems(List<ActionMenuItem> uiItems)
    {
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

        return ReorderRootSections(finalItems);
    }

    // Reorders contiguous sections (each starting at an IsSectionHeader item) according to the user's
    // saved ActionMenuGroupOrder, most-preferred first. A section whose SectionGroupId isn't listed yet
    // falls back to its current position -- relying on List.Sort/OrderBy being STABLE, so unlisted
    // sections keep their natural discovery order (built-in first, then dynamic providers by Priority)
    // relative to each other. Safe no-op for submenu-level lists, which have at most one trivial section
    // with an empty SectionGroupId.
    private static List<ActionMenuItem> ReorderRootSections(List<ActionMenuItem> items)
    {
        var headerIndexes = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSectionHeader)
                headerIndexes.Add(i);
        }

        if (headerIndexes.Count < 2)
            return items;

        var order = Core.UserSettings.Load().ActionMenuGroupOrder;

        var sections = new List<List<ActionMenuItem>>();
        for (var i = 0; i < headerIndexes.Count; i++)
        {
            var start = headerIndexes[i];
            var end = i + 1 < headerIndexes.Count ? headerIndexes[i + 1] : items.Count;
            sections.Add(items.GetRange(start, end - start));
        }

        var reordered = sections
            .OrderBy(section =>
            {
                var rank = order.IndexOf(section[0].SectionGroupId);
                return rank >= 0 ? rank : int.MaxValue;
            })
            .SelectMany(section => section)
            .ToList();

        return reordered;
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
