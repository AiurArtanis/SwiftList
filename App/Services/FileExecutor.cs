using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SwiftList.Core;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;
namespace SwiftList.App.Services;

public static class FileExecutor
{
    public static void OpenFileOrFolder(string path, string currentSearchText = "", Action? onHideWindow = null) => OpenFileOrFolderCore(path, currentSearchText, onHideWindow, asAdmin: false);

    public static void OpenFileOrFolderAsAdmin(string path, string currentSearchText = "", Action? onHideWindow = null) => OpenFileOrFolderCore(path, currentSearchText, onHideWindow, asAdmin: true);

    private static void OpenFileOrFolderCore(string path, string currentSearchText, Action? onHideWindow, bool asAdmin)
    {
        if (path == "__NO_RESULTS__")
            return;
        if (path == "__SHOW_MORE__")
        {
            var searchWin = new SearchWindow(currentSearchText);
            searchWin.Show();
            onHideWindow?.Invoke();
            return;
        }

        try
        {
            // Web-address (http/https) favorites: hand straight to the default browser. They aren't files,
            // so the File/Directory.Exists check below would wrongly report "not found".
            if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return;
            }

            var isVirtual = path.StartsWith("::") || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
            if (isVirtual || File.Exists(path) || Directory.Exists(path))
            {
                var isFile = !isVirtual && File.Exists(path);
                ProcessStartInfo startInfo;

                if (asAdmin)
                {
                    if (isFile)
                    {
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        var isExecutable = ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".com" || ext == ".scr" || ext == ".msi" || ext == ".lnk";

                        if (isExecutable)
                        {
                            startInfo = new ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                        }
                        else
                        {
                            // The "runas" verb applies to executables, not documents, so we can't just
                            // elevate the file directly. Resolve the file's associated program (e.g.
                            // Notepad++) and elevate THAT with the file as its argument, so admin-open
                            // uses the same handler as a normal open.
                            var associatedExe = TryGetAssociatedExecutable(path);
                            if (!string.IsNullOrEmpty(associatedExe))
                            {
                                startInfo = new ProcessStartInfo
                                {
                                    FileName = associatedExe,
                                    Arguments = $"\"{path}\"",
                                    UseShellExecute = true,
                                    Verb = "runas"
                                };
                            }
                            else
                            {
                                // No association resolved — bring up the shell "Open with" dialog, but run
                                // it ELEVATED (runas). The program the user then picks is launched as a
                                // child of the elevated dialog and inherits admin rights, which matches the
                                // admin-open intent instead of degrading to a normal launch.
                                // OpenWith.exe is a normal exe that pops the same "Open with" dialog and
                                // takes a standard quoted path argument (so spaces just work). Elevating it
                                // means the program the user picks inherits admin rights.
                                startInfo = new ProcessStartInfo
                                {
                                    FileName = "OpenWith.exe",
                                    Arguments = $"\"{path}\"",
                                    UseShellExecute = true,
                                    Verb = "runas"
                                };
                            }
                        }
                    }
                    else
                    {
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/k cd /d \"{path}\"",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                    }
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    };
                }

                if (isFile && !asAdmin)
                {
                    var workingDirectory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(workingDirectory))
                    {
                        if (Directory.Exists(workingDirectory))
                        {
                            startInfo.WorkingDirectory = workingDirectory;
                        }
                    }
                }

                try
                {
                    Process.Start(startInfo);
                }

                catch (Exception startEx)
                {
                    Logger.Log($"[FileExecutor] Process.Start failed for '{path}': {startEx.Message}", LogLevel.Error);
                    throw;
                }
            }

            else
            {
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_NotExist"], path), TranslationManager.Instance["Executor_PromptTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] OpenFileOrFolder failed for '{path}': {ex}", LogLevel.Error);
            MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void LocateInExplorer(string path)
    {
        try
        {
            // SHOpenFolderAndSelectItems routes through the shell so it respects the user's
            // default file manager (e.g. Directory Opus) rather than always opening explorer.exe.
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

    private enum AssocStr { Executable = 2 }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(uint flags, AssocStr str, string pszAssoc, string? pszExtra, System.Text.StringBuilder? pszOut, ref uint pcchOut);

    /// <summary>
    /// Resolves the executable associated with a file's extension (the program a normal double-click
    /// would launch). Returns null when there is no real association, so callers can fall back.
    /// </summary>
    private static string? TryGetAssociatedExecutable(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return null;

            uint length = 1024;
            var sb = new System.Text.StringBuilder((int)length);
            if (AssocQueryString(0, AssocStr.Executable, ext, null, sb, ref length) != 0) // S_OK == 0
                return null;

            var exe = sb.ToString();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return null;

            // Windows hands back a generic launcher when there is no real handler; don't elevate those.
            var name = Path.GetFileName(exe).ToLowerInvariant();
            if (name is "openwith.exe" or "rundll32.exe" or "applicationframehost.exe")
                return null;

            return exe;
        }
        catch
        {
            return null;
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
