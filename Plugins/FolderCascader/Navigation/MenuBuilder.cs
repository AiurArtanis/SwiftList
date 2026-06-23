using System.IO;
using SwiftList.PluginSdk;

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
                    items.Add(new DynamicMenuItem
                    {
                        Text = GetDisplayName(folder.Path, folder.Name),
                        HasSubMenu = true,
                        SubMenuHandle = provider.AllocateHandle(folder.Path),
                        HBitmapItem = IntPtr.Zero
                    });
                }
            }

            var showHistory = PluginSettingsService.GetSetting(
                "SwiftList.Plugins.FolderCascader",
                "ShowHistory",
                true);

            if (showHistory)
            {
                if (items.Count > 0)
                {
                    items.Add(new DynamicMenuItem { IsSeparator = true });
                }
                items.Add(new DynamicMenuItem
                {
                    Text = TranslationService.Get("FolderCascader_History") ?? "History",
                    HasSubMenu = true,
                    SubMenuHandle = provider.AllocateHandle("foldercascader://history"),
                    HBitmapItem = Helper.HistoryHBitmap
                });
            }

            return items;
        }

        if (provider.TryGetPath(hMenu, out var path) && path != null)
        {
            var items = new List<DynamicMenuItem>();
            if (path == "foldercascader://history")
            {
                var recentPaths = Helper.GetHistoryPaths();
                foreach (var rpath in recentPaths)
                {
                    if (string.IsNullOrWhiteSpace(rpath)) continue;
                    if (Directory.Exists(rpath))
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
                    items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoHistory") ?? "(No history)", IsDisabled = true });
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
                        var shellType = Type.GetTypeFromProgID("Shell.Application");
                        if (shellType != null)
                        {
                            var shell = Activator.CreateInstance(shellType);
                            if (shell != null)
                            {
                                dynamic dShell = shell;
                                var fullShellPath = scanPath.StartsWith("::") ? "shell:::" + scanPath : scanPath;
                                dynamic folder = dShell.NameSpace(fullShellPath);
                                if (folder != null)
                                {
                                    foreach (var item in folder.Items())
                                    {
                                        string p = item.Path;
                                        string name = item.Name;
                                        if (!string.IsNullOrEmpty(p))
                                        {
                                            if (item.IsFolder)
                                            {
                                                items.Add(new DynamicMenuItem
                                                {
                                                    Text = name,
                                                    HasSubMenu = true,
                                                    SubMenuHandle = provider.AllocateHandle(p),
                                                    HBitmapItem = IntPtr.Zero
                                                });
                                            }
                                            else
                                            {
                                                items.Add(new DynamicMenuItem
                                                {
                                                    Text = name,
                                                    CommandId = provider.AllocateCommand(p),
                                                    HBitmapItem = IntPtr.Zero
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (items.Count == 0)
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = TranslationService.Get("FolderCascader_EmptyFolder") ?? "(Empty)",
                            IsDisabled = true
                        });
                    }
                }
                catch
                {
                    items.Add(new DynamicMenuItem
                    {
                        Text = TranslationService.Get("FolderCascader_EmptyFolder") ?? "(Empty)",
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
        if (path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch { return path; }
    }
}
