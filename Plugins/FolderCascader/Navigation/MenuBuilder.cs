using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;

namespace SwiftList.Plugins.FolderCascader.Navigation;

public static class MenuBuilder
{
    public static IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu, Provider provider)
    {
        IconBitmapCache.EnsureIcons();

        if (hMenu == IntPtr.Zero)
        {
            provider.ClearSession();
            var items = new List<DynamicMenuItem>();

            // Unpersisted falls back to FolderCascaderPlugin's own schema DefaultValue automatically
            // -- see PluginManager.GetSettingFunc -- so there's no separate hardcoded default here.
            var folders = PluginSettingsService.GetSetting(
                "SwiftList.Plugins.FolderCascader",
                "Folders",
                new List<FolderCascaderPlugin.FolderConfigItem>());

            if (folders != null)
            {
                AddFolderItems(items, folders, Array.Empty<string>(), provider);
            }

            var showFavorites = PluginSettingsService.GetSetting(
                "SwiftList.Plugins.FolderCascader",
                "ShowFavorites",
                true);

            var showHistory = PluginSettingsService.GetSetting(
                "SwiftList.Plugins.FolderCascader",
                "ShowHistory",
                true);

            var favoritesList = FavoritesService.GetFavorites()
                 .Where(p => !string.IsNullOrEmpty(p.Path))
                 .ToList();

            if (showFavorites && favoritesList.Count > 0)
            {
                if (items.Count > 0 && !items.Last().IsSeparator)
                {
                    items.Add(new DynamicMenuItem { IsSeparator = true });
                }
                items.Add(new DynamicMenuItem
                {
                    Text = TranslationService.Get("FolderCascader_Favorites"),
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle("foldercascader://favorites"),
                    HBitmapItem = IconBitmapCache.FavoritesHBitmap
                });
            }

            if (showHistory && HistoryService.GetHistoryEntries().Take(30).ToList().Count > 0)
            {
                if (items.Count > 0 && !items.Last().IsSeparator)
                {
                    items.Add(new DynamicMenuItem { IsSeparator = true });
                }
                items.Add(new DynamicMenuItem
                {
                    Text = TranslationService.Get("FolderCascader_History"),
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle("foldercascader://history"),
                    HBitmapItem = IconBitmapCache.HistoryHBitmap
                });
            }

            while (items.Count > 0 && items.Last().IsSeparator)
            {
                items.RemoveAt(items.Count - 1);
            }

            return items;
        }

        if (provider.TryGetPath(hMenu, out var path) && path != null)
        {
            var items = new List<DynamicMenuItem>();
            var favoritesList = FavoritesService.GetFavorites()
                .Where(p => !string.IsNullOrEmpty(p.Path))
                .ToList();

            if (path == "foldercascader://history")
            {
                var recentEntries = HistoryService.GetHistoryEntries().Take(30).ToList();
                foreach (var entry in recentEntries)
                {
                    var rpath = entry.Path;
                    if (string.IsNullOrWhiteSpace(rpath)) continue;

                    // An app-type entry is always a launchable leaf, never a browsable folder -- and
                    // its path (a real exe path, or a virtual shell:AppsFolder\{AUMID} id) can't be
                    // existence-checked with Directory.Exists/File.Exists the way a real path can.
                    if (entry.Kind == HistoryEntryKind.Application)
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = GetDisplayName(rpath, ""),
                            CommandId = provider.AllocateCommand(rpath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                    else if (Directory.Exists(rpath))
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = GetDisplayName(rpath, ""),
                            HasSubMenu = true,
                            SubMenuHandle = provider.AllocateHandle(rpath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                    else if (File.Exists(rpath))
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = Path.GetFileName(rpath) + $" ({Path.GetDirectoryName(rpath)})",
                            CommandId = provider.AllocateCommand(rpath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                }
                if (items.Count == 0)
                    items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoHistory"), IsDisabled = true });
            }
            else if (path == "foldercascader://favorites")
            {
                foreach (var favItem in favoritesList)
                {
                    var favPath = favItem.Path;
                    var isVirtual = favPath.StartsWith("::") || favPath.StartsWith("shell:");
                    if (isVirtual || Directory.Exists(favPath))
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = GetDisplayName(favPath, favItem.Name),
                            HasSubMenu = true,
                            SubMenuHandle = provider.AllocateHandle(favPath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                    else if (File.Exists(favPath))
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = string.IsNullOrWhiteSpace(favItem.Name) ? Path.GetFileName(favPath) : favItem.Name,
                            CommandId = provider.AllocateCommand(favPath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                    else if (IsWebUrl(favPath))
                    {
                        // Web-address favorite: a leaf command item. The host renders the globe icon and
                        // opens it in the browser (both keyed off the http/https path).
                        items.Add(new DynamicMenuItem
                        {
                            Text = string.IsNullOrWhiteSpace(favItem.Name) ? favPath : favItem.Name,
                            CommandId = provider.AllocateCommand(favPath),
                            HBitmapItem = IntPtr.Zero
                        });
                    }
                }
                if (items.Count == 0)
                    items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoFavorites"), IsDisabled = true });
            }
            else if (TryDecodeCategoryPath(path, out var categoryPrefix))
            {
                // A submenu category node (see AddFolderItems), not a real filesystem path -- reload
                // the same Folders setting the root level did and re-run the grouping logic scoped to
                // this category's prefix, same as CustomCommandsQuickNavProvider re-partitions its own
                // flat list on every submenu expansion instead of building a tree once up front.
                var folders = PluginSettingsService.GetSetting(
                    "SwiftList.Plugins.FolderCascader",
                    "Folders",
                    new List<FolderCascaderPlugin.FolderConfigItem>());
                if (folders != null)
                {
                    AddFolderItems(items, folders, categoryPrefix, provider);
                }
                while (items.Count > 0 && items.Last().IsSeparator)
                {
                    items.RemoveAt(items.Count - 1);
                }
                if (items.Count == 0)
                    items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_EmptyFolder"), IsDisabled = true });
            }
            else
            {
                try
                {
                    var scanPath = path;
                    if (scanPath.StartsWith("::") || scanPath.StartsWith("shell:"))
                    {
                        var resolved = ShellPathHelper.TryResolveVirtualPath(scanPath);
                        if (Directory.Exists(resolved))
                        {
                            scanPath = resolved;
                        }
                    }

                    if (Directory.Exists(scanPath))
                    {
                        var subDirs = Directory.GetDirectories(scanPath)
                            .Where(d =>
                            {
                                try { return (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                                catch { return false; }
                            })
                            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
                        var subFiles = Directory.GetFiles(scanPath)
                            .Where(f =>
                            {
                                try { return (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                                catch { return false; }
                            })
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

                        foreach (var dir in subDirs)
                        {
                            items.Add(new DynamicMenuItem
                            {
                                Text = Path.GetFileName(dir),
                                HasSubMenu = true,
                                SubMenuHandle = provider.AllocateHandle(dir),
                                HBitmapItem = IntPtr.Zero
                            });
                        }
                        foreach (var file in subFiles)
                        {
                            items.Add(new DynamicMenuItem
                            {
                                Text = Path.GetFileName(file),
                                CommandId = provider.AllocateCommand(file),
                                HBitmapItem = IntPtr.Zero
                            });
                        }
                    }
                    else if (scanPath.StartsWith("::") || scanPath.StartsWith("shell:"))
                    {
                        ShellEnumerator.EnumerateShellFolder(scanPath, items, provider);
                    }

                    if (items.Count == 0)
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = TranslationService.Get("FolderCascader_EmptyFolder"),
                            IsDisabled = true
                        });
                    }
                }
                catch
                {
                    items.Add(new DynamicMenuItem
                    {
                        Text = TranslationService.Get("FolderCascader_EmptyFolder"),
                        IsDisabled = true
                    });
                }
            }
            return items;
        }

        return Enumerable.Empty<DynamicMenuItem>();
    }

    private static string GetDisplayName(string path, string customName)
    {
        if (!string.IsNullOrWhiteSpace(customName)) return customName;
        // "shell:" covers both the "shell:::{CLSID}" virtual-folder form and "shell:AppsFolder\{AUMID}"
        // (packaged apps) -- not just the CLSID form -- matching the isVirtual check already used for
        // favorites above.
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch { return path; }
    }

    private static bool IsWebUrl(string path)
        => Uri.TryCreate(path?.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private const string CategoryPathPrefix = "foldercascader://category/";

    // Groups configured folders by their SubMenu field and appends the items belonging at exactly
    // "prefix" depth -- a leaf (Name/Path) entry for folders whose SubMenu matches prefix exactly, or
    // (at most once per distinct next segment) a HasSubMenu category entry for folders nested deeper.
    // Same re-partition-a-flat-list-on-every-expansion technique CustomCommandsQuickNavProvider uses
    // for its own SubMenu field, rather than building a tree once up front.
    internal static void AddFolderItems(List<DynamicMenuItem> items, List<FolderCascaderPlugin.FolderConfigItem> folders, string[] prefix, Provider provider)
    {
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var segments = SplitSubMenuPath(folder.SubMenu);
            if (!StartsWithPrefix(segments, prefix)) continue;

            if (segments.Length > prefix.Length)
            {
                var category = segments[prefix.Length];
                if (!seenCategories.Add(category)) continue;

                var childPrefix = new string[prefix.Length + 1];
                Array.Copy(prefix, childPrefix, prefix.Length);
                childPrefix[prefix.Length] = category;

                items.Add(new DynamicMenuItem
                {
                    Text = category,
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle(EncodeCategoryPath(childPrefix)),
                    HBitmapItem = IconBitmapCache.CategoryHBitmap,
                    // QuickNavigationMenu's own root-level click-suppression (isRootItem && HasSubMenu)
                    // only ever applies at the very top level -- every nested submenu (any category one
                    // level deep or more, e.g. this one when prefix.Length > 0) is built through
                    // QuickNavigationSubMenuLoader, which never passes isRootItem: true. IsActionable is
                    // the only gate that reaches those, so it has to be set explicitly here regardless of
                    // depth, not just relied on implicitly like the root case.
                    IsActionable = false
                });
                continue;
            }

            // segments.Length == prefix.Length: this entry belongs exactly at the current level.
            if (folder.Path == "-" || folder.Name == "-")
            {
                items.Add(new DynamicMenuItem { IsSeparator = true });
                continue;
            }
            if (string.IsNullOrWhiteSpace(folder.Path)) continue;
            var pathExists = true;
            if (!folder.Path.StartsWith("::") && !folder.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                pathExists = Directory.Exists(folder.Path);
            }
            items.Add(new DynamicMenuItem
            {
                Text = GetDisplayName(folder.Path, folder.Name),
                HasSubMenu = pathExists,
                SubMenuHandle = pathExists ? provider.AllocateHandle(folder.Path) : IntPtr.Zero,
                HBitmapItem = IntPtr.Zero,
                IsDisabled = !pathExists
            });
        }
    }

    // Empty segments (e.g. "a//b", "a/", "/a") are dropped rather than producing an empty-named
    // category or erroring -- a stray typo in the config shouldn't break navigation.
    internal static string[] SplitSubMenuPath(string subMenu) =>
        string.IsNullOrWhiteSpace(subMenu)
            ? Array.Empty<string>()
            : subMenu.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Case-sensitive by design, matching CustomCommandsQuickNavProvider's own SubMenu grouping:
    // "Tools" and "tools" are two distinct categories, never merged.
    internal static bool StartsWithPrefix(string[] segments, string[] prefix)
    {
        if (segments.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(segments[i], prefix[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    internal static string EncodeCategoryPath(string[] segments) => CategoryPathPrefix + string.Join("/", segments);

    internal static bool TryDecodeCategoryPath(string path, out string[] segments)
    {
        if (!path.StartsWith(CategoryPathPrefix, StringComparison.Ordinal))
        {
            segments = Array.Empty<string>();
            return false;
        }
        segments = path.Substring(CategoryPathPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return true;
    }
}
