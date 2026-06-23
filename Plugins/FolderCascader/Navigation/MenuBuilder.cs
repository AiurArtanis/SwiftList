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

            items.Add(new DynamicMenuItem
            {
                Text = ShellPathHelper.GetVirtualFolderDisplayName("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}", "Quick Access"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}"),
                HBitmapItem = IntPtr.Zero
            });

            items.Add(new DynamicMenuItem
            {
                Text = ShellPathHelper.GetVirtualFolderDisplayName("shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}", "This PC"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}"),
                HBitmapItem = IntPtr.Zero
            });

            items.Add(new DynamicMenuItem
            {
                Text = ShellPathHelper.GetVirtualFolderDisplayName("shell:::{450d8fba-ad25-11d0-98a8-0800361b1103}", "Documents"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("shell:::{450d8fba-ad25-11d0-98a8-0800361b1103}"),
                HBitmapItem = IntPtr.Zero
            });

            items.Add(new DynamicMenuItem
            {
                Text = TranslationService.Get("QuickNav_History"),
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle("quicknav://history"),
                HBitmapItem = Helper.HistoryHBitmap
            });

            return items;
        }

        if (provider.TryGetPath(hMenu, out var path) && path != null)
        {
            var items = new List<DynamicMenuItem>();
            if (path == "quicknav://history")
            {
                var recentPaths = Helper.GetHistoryPaths();
                foreach (var rpath in recentPaths)
                {
                    if (Directory.Exists(rpath))
                    {
                        items.Add(new DynamicMenuItem
                        {
                            Text = rpath,
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
                    items.Add(new DynamicMenuItem { Text = TranslationService.Get("QuickNav_NoHistory") ?? "(No history)", IsDisabled = true });
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
                            Text = TranslationService.Get("QuickNav_EmptyFolder") ?? "(Empty)",
                            IsDisabled = true
                        });
                    }
                }
                catch
                {
                    items.Add(new DynamicMenuItem
                    {
                        Text = TranslationService.Get("QuickNav_EmptyFolder") ?? "(Empty)",
                        IsDisabled = true
                    });
                }
            }
            return items;
        }

        return Enumerable.Empty<DynamicMenuItem>();
    }
}
