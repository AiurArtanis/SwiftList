using System.IO;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.FolderCascader.Navigation;

public static class CommandExecutor
{
    public static void Execute(ISearchResult result, string path)
    {
        var targetDir = result.IsDir ? result.FullPath : Path.GetDirectoryName(result.FullPath);
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            targetDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path == "powershell")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch PowerShell: {ex.Message}", LogLevel.Error);
            }
        }
        else if (path == "cmd")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch Command Prompt: {ex.Message}", LogLevel.Error);
            }
        }
        else if (path == "options")
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var settingsWindowType = System.Reflection.Assembly.GetExecutingAssembly().GetType("SwiftList.App.Views.Settings.SettingsWindow") ?? (System.Reflection.Assembly.GetEntryAssembly()?.GetType("SwiftList.App.Views.Settings.SettingsWindow"));
                        if (settingsWindowType != null)
                        {
                            var win = Activator.CreateInstance(settingsWindowType) as System.Windows.Window;
                            win?.Show();
                        }
                    }
                    catch { }
                }));
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch Options: {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to execute {path}: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
