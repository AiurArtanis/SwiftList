using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SwiftList.Core;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;

namespace SwiftList.App.Services
{
    public static class FileExecutor
    {
        public static void OpenFileOrFolder(string path, string currentSearchText = "", Action? onHideWindow = null)
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
                string physicalPath = NetworkDriveResolver.ResolveToPhysicalPath(path);
                if (File.Exists(physicalPath) || Directory.Exists(physicalPath))
                {
                    bool isFile = File.Exists(physicalPath);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    };

                    if (isFile)
                    {
                        string? workingDirectory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(workingDirectory))
                        {
                            string physicalWorkingDir = NetworkDriveResolver.ResolveToPhysicalPath(workingDirectory);
                            if (Directory.Exists(workingDirectory))
                            {
                                startInfo.WorkingDirectory = workingDirectory;
                            }
                            else if (Directory.Exists(physicalWorkingDir))
                            {
                                startInfo.WorkingDirectory = physicalWorkingDir;
                            }
                        }
                    }

                    try
                    {
                        Process.Start(startInfo);
                    }
                    catch (Exception startEx)
                    {
                        Logger.Log($"[FileExecutor] Process.Start failed for '{path}', retrying with physical path '{physicalPath}': {startEx.Message}");
                        startInfo.FileName = physicalPath;
                        if (isFile)
                        {
                            string? physicalWorkingDir = Path.GetDirectoryName(physicalPath);
                            if (!string.IsNullOrWhiteSpace(physicalWorkingDir) && Directory.Exists(physicalWorkingDir))
                            {
                                startInfo.WorkingDirectory = physicalWorkingDir;
                            }
                        }
                        Process.Start(startInfo);
                    }
                }
                else
                {
                    MessageBox.Show(string.Format(TranslationManager.Instance["Executor_NotExist"], path), TranslationManager.Instance["Executor_PromptTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileExecutor] OpenFileOrFolder failed for '{path}': {ex}");
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void LocateInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileExecutor] Locate in explorer failed for '{path}': {ex.Message}");
                MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero) return false;

            try
            {
                dynamic? window = FindExplorerWindow(explorerHwnd);
                if (window == null) return false;

                string? targetFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
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
                Logger.Log($"[FileExecutor] Locate in existing explorer failed for '{path}': {ex.Message}");
                return false;
            }
        }

        public static bool TrySelectItemInExistingExplorer(string path, IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero) return false;

            try
            {
                dynamic? window = FindExplorerWindow(explorerHwnd);
                if (window == null) return false;

                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return false;

                dynamic folder = window.Document.Folder;
                dynamic? item = folder.ParseName(name);
                if (item == null) return false;

                const int svsiSelect = 0x1;
                const int svsiDeselectOthers = 0x4;
                const int svsiEnsureVisible = 0x8;
                window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileExecutor] Select item in existing explorer failed for '{path}': {ex.Message}");
                return false;
            }
        }

        private static dynamic? FindExplorerWindow(IntPtr explorerHwnd)
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return null;

            dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
            int count = shellWindows.Count;

            for (int i = 0; i < count; i++)
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

                string name = Path.GetFileName(path);
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
                Logger.Log($"[FileExecutor] Select item in existing explorer failed for '{path}': {ex.Message}");
            }
        }

        public static string? GetDialogFolderPath(IntPtr dialogHwnd) => SwiftList.Core.Hook.FileDialogNavigator.GetDialogFolderPath(dialogHwnd);

        public static void NavigateDialog(IntPtr targetEdit, string fullPath) => SwiftList.Core.Hook.FileDialogNavigator.NavigateDialog(targetEdit, fullPath);
    }
}
