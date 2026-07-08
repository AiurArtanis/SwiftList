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

        // Web-address (http/https) favorites: hand straight to the default browser, no filesystem I/O
        // needed, so no reason to leave the UI thread for these.
        if (Helpers.FavoriteUrlHelper.IsWebUrl(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileExecutor] OpenFileOrFolder failed for '{path}': {ex}", LogLevel.Error);
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        // Everything below can block for seconds on a slow or heavily-indexed network share
        // (File.Exists/Directory.Exists have no timeout) -- run it off the UI thread so launching
        // something doesn't freeze the whole app while a background scan is hammering the same share.
        // Process.Start itself doesn't need the UI thread either (UseShellExecute hands off to the shell
        // and returns); CustomMessageBox.Show already marshals itself back when called off-thread.
        Task.Run(() => LaunchExistingPath(path, asAdmin));
    }

    private static void LaunchExistingPath(string path, bool asAdmin)
    {
        try
        {
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

    public static void LocateInExplorer(string path) => ExplorerLocateHelper.LocateInExplorer(path);

    public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd) => ExplorerLocateHelper.TryLocateInExistingExplorer(path, explorerHwnd);

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
}
