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
        Helper.EnsureIcons();

        if (hMenu == IntPtr.Zero)
        {
            provider.ClearSession();
            var items = new List<DynamicMenuItem>();

            var defaults = new List<FolderCascaderPlugin.FolderConfigItem>
            {
                new FolderCascaderPlugin.FolderConfigItem { Name = "", Path = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}" },
                new FolderCascaderPlugin.FolderConfigItem { Name = "", Path = "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}" },
                new FolderCascaderPlugin.FolderConfigItem { Name = "", Path = "shell:::{450d8fba-ad25-11d0-98a8-0800361b1103}" }
            };

            var folders = PluginSettingsService.GetSetting(
                "SwiftList.Plugins.FolderCascader",
                "Folders",
                defaults);

            if (folders != null)
            {
                foreach (var folder in folders)
                {
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
                    HBitmapItem = Helper.FavoritesHBitmap
                });
            }

            if (showHistory && Helper.GetHistoryPaths().Count > 0)
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
                    HBitmapItem = Helper.HistoryHBitmap
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
                var recentPaths = Helper.GetHistoryPaths();
                foreach (var rpath in recentPaths)
                {
                    if (string.IsNullOrWhiteSpace(rpath)) continue;

                    // An app-type entry is always a launchable leaf, never a browsable folder -- and
                    // its raw path (a real exe path, or a virtual shell:AppsFolder\{AUMID} id) can't be
                    // existence-checked with Directory.Exists/File.Exists the way a real path can.
                    if (HistoryService.IsAppEntry(rpath))
                    {
                        var appPath = HistoryService.GetRawPath(rpath);
                        items.Add(new DynamicMenuItem
                        {
                            Text = GetDisplayName(appPath, ""),
                            CommandId = provider.AllocateCommand(appPath),
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
}
