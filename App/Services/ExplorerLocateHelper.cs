using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SwiftList.Core;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SwiftList.App.Services;

// "Select this item in Explorer" -- split out of FileExecutor to keep that file under the line-count
// limit. Routes through the shell (SHOpenFolderAndSelectItems / an existing window's own Navigate2) so
// it respects the user's default file manager instead of always opening explorer.exe.
internal static class ExplorerLocateHelper
{
    public static void LocateInExplorer(string path)
    {
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0)
            {
                SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                Marshal.FreeCoTaskMem(pidl);
                return;
            }
        }
        catch { }

        // Fallback
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
    {
        if (explorerHwnd == IntPtr.Zero) return false;
        try
        {
            dynamic? window = FindExplorerWindow(explorerHwnd);
            if (window == null) return false;
            var targetFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                return false;
            }

            window.Navigate2(targetFolder);
            if (File.Exists(path))
            {
                SelectItemInExplorerLater(path, explorerHwnd);
            }

            return true;
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private static dynamic? FindExplorerWindow(IntPtr explorerHwnd)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindowsType == null) return null;
        dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
        int count = shellWindows.Count;
        for (var i = 0; i < count; i++)
        {
            try
            {
                dynamic? window = shellWindows.Item(i);
                if (window == null) continue;
                if ((IntPtr)window.HWND == explorerHwnd)
                {
                    return window;
                }
            }

            catch { }
        }

        return null;
    }

    private static async void SelectItemInExplorerLater(string path, IntPtr explorerHwnd)
    {
        await Task.Delay(250);

        try
        {
            dynamic? window = FindExplorerWindow(explorerHwnd);
            if (window == null) return;
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return;
            dynamic folder = window.Document.Folder;
            dynamic? item = folder.ParseName(name);
            if (item == null) return;
            const int svsiSelect = 0x1;
            const int svsiDeselectOthers = 0x4;
            const int svsiEnsureVisible = 0x8;
            window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Select item in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
        }
    }
}
