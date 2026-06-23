using SwiftList.PluginSdk;

namespace SwiftList.Plugins.FolderCascader.Navigation;

public static class ShellEnumerator
{
    public static void EnumerateShellFolder(string scanPath, List<DynamicMenuItem> items, Provider provider)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic dShell = shell;
            var fullShellPath = scanPath.StartsWith("::") ? "shell:::" + scanPath : scanPath;
            dynamic folder = dShell.NameSpace(fullShellPath);
            if (folder == null) return;

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
        catch { }
    }
}
