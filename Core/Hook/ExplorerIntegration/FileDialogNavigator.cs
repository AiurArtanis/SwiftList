using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.Core;

namespace SwiftList.Core.Hook
{
    public static class FileDialogNavigator
    {
        public static void NavigateDialog(IntPtr targetEdit, string fullPath)
        {
            try
            {
                if (targetEdit != IntPtr.Zero)
                {
                    IntPtr dialogHwnd = FindDialogWindow(targetEdit);
                    if (dialogHwnd != IntPtr.Zero)
                    {
                        // For directory paths, append a trailing backslash so the dialog
                        // navigates INTO the folder instead of selecting/opening it.
                        if (Directory.Exists(fullPath) && !fullPath.EndsWith("\\"))
                            fullPath += "\\";

                        string? currentPath = GetDialogFolderPath(dialogHwnd);
                        if (currentPath != null && string.Equals(currentPath.TrimEnd('\\'), fullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Log($"[FileDialogNavigator] Dialog is already at target path: '{fullPath}'. Skipping navigation.");
                            return;
                        }

                        SetTextAndNotify(targetEdit, fullPath);

                            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                            {
                                System.Threading.Thread.Sleep(150);
                                IntPtr currentActive = ExplorerNativeHooks.GetForegroundWindow();
                                if (currentActive == dialogHwnd)
                                {
                                    uint targetThread = ExplorerNativeHooks.GetWindowThreadProcessId(targetEdit, out uint _);
                                    uint currentThread = ExplorerNativeHooks.GetCurrentThreadId();
                                    bool attached = false;
                                    try
                                    {
                                        if (targetThread != 0 && targetThread != currentThread)
                                        {
                                            attached = ExplorerNativeHooks.AttachThreadInput(currentThread, targetThread, true);
                                        }

                                        ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                                        ExplorerNativeHooks.SetFocus(targetEdit);

                                        // Send Enter key to the focused edit box to trigger the dialog's native navigation.
                                        // This ensures it enters directories when a trailing \ is present, rather than confirming.
                                        ExplorerNativeHooks.PostMessage(targetEdit, FileDialogNativeMethods.WM_KEYDOWN, (IntPtr)FileDialogNativeMethods.VK_RETURN, IntPtr.Zero);
                                        ExplorerNativeHooks.PostMessage(targetEdit, FileDialogNativeMethods.WM_KEYUP, (IntPtr)FileDialogNativeMethods.VK_RETURN, IntPtr.Zero);

                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.WM_LBUTTONDOWN, (IntPtr)1, IntPtr.Zero);
                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                                        ExplorerNativeHooks.PostMessage(targetEdit, ExplorerNativeHooks.EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                                    }
                                    finally
                                    {
                                        if (attached)
                                        {
                                            ExplorerNativeHooks.AttachThreadInput(currentThread, targetThread, false);
                                        }
                                    }
                                }
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileDialogNavigator] Error during dialog navigation: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
        }

        private static void SetTextAndNotify(IntPtr editHwnd, string text)
        {
            FileDialogNativeMethods.SendMessage(editHwnd, FileDialogNativeMethods.WM_SETTEXT, IntPtr.Zero, text);
            
            IntPtr parent = FileDialogNativeMethods.GetParent(editHwnd);
            int ctrlId = FileDialogNativeMethods.GetDlgCtrlID(editHwnd);
            if (parent != IntPtr.Zero)
            {
                IntPtr wParamChange = (IntPtr)((FileDialogNativeMethods.EN_CHANGE << 16) | (uint)ctrlId);
                FileDialogNativeMethods.SendMessageIntPtr(parent, FileDialogNativeMethods.WM_COMMAND, wParamChange, editHwnd);
            }
        }

        private static IntPtr FindDialogWindow(IntPtr hwnd)
        {
            IntPtr current = hwnd;
            while (current != IntPtr.Zero)
            {
                var sb = new StringBuilder(256);
                FileDialogNativeMethods.GetClassName(current, sb, sb.Capacity);
                if (sb.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }
                current = FileDialogNativeMethods.GetParent(current);
            }
            return IntPtr.Zero;
        }

        private static IntPtr FindBreadcrumbParent(IntPtr parent)
        {
            IntPtr child = FileDialogNativeMethods.FindWindowEx(parent, IntPtr.Zero, null, null);
            while (child != IntPtr.Zero)
            {
                var classNameSb = new StringBuilder(256);
                FileDialogNativeMethods.GetClassName(child, classNameSb, classNameSb.Capacity);
                string className = classNameSb.ToString();

                if (className.Equals("Breadcrumb Parent", StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                IntPtr subParent = FindBreadcrumbParent(child);
                if (subParent != IntPtr.Zero)
                {
                    return subParent;
                }

                child = FileDialogNativeMethods.FindWindowEx(parent, child, null, null);
            }
            return IntPtr.Zero;
        }

        private static string GetLocalizedFolderName(string physicalPath)
        {
            try
            {
                var shfi = new FileDialogNativeMethods.SHFILEINFO();
                IntPtr res = FileDialogNativeMethods.SHGetFileInfo(physicalPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), FileDialogNativeMethods.SHGFI_DISPLAYNAME);
                if (res != IntPtr.Zero && !string.IsNullOrEmpty(shfi.szDisplayName))
                {
                    return shfi.szDisplayName.Trim();
                }
            }
            catch { }
            return Path.GetFileName(physicalPath) ?? string.Empty;
        }

        private static readonly Environment.SpecialFolder[] _trackedSpecialFolders = new[]
        {
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.UserProfile
        };

        private static string ResolveSpecialFolder(string name)
        {
            name = name.Trim();

            foreach (var folderType in _trackedSpecialFolders)
            {
                try
                {
                    string path = Environment.GetFolderPath(folderType);
                    if (string.IsNullOrEmpty(path)) continue;

                    string dirName = Path.GetFileName(path);
                    string localizedName = GetLocalizedFolderName(path);

                    if (string.Equals(name, dirName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, localizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                catch { }
            }

            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloadsPath = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    string dirName = Path.GetFileName(downloadsPath);
                    string localizedName = GetLocalizedFolderName(downloadsPath);

                    if (string.Equals(name, dirName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, localizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return downloadsPath;
                    }
                }
            }
            catch { }

            return name;
        }

        public static string? GetDialogFolderPath(IntPtr dialogHwnd)
        {
            try
            {
                if (dialogHwnd == IntPtr.Zero) return null;

                IntPtr breadcrumbParent = FindBreadcrumbParent(dialogHwnd);
                if (breadcrumbParent != IntPtr.Zero)
                {
                    IntPtr child = FileDialogNativeMethods.FindWindowEx(breadcrumbParent, IntPtr.Zero, "ToolbarWindow32", null);
                    while (child != IntPtr.Zero)
                    {
                        var textSb = new StringBuilder(1024);
                        FileDialogNativeMethods.SendMessageStringBuilder(child, FileDialogNativeMethods.WM_GETTEXT, (IntPtr)textSb.Capacity, textSb);
                        string text = textSb.ToString().Trim();

                        string potentialPath = text;
                        int colonIndex = text.IndexOf(':');
                        if (colonIndex >= 0)
                        {
                            bool isDriveLetter = false;
                            if (colonIndex == 1 && text.Length >= 2)
                            {
                                char letter = text[0];
                                if ((letter >= 'a' && letter <= 'z') || (letter >= 'A' && letter <= 'Z'))
                                {
                                    isDriveLetter = true;
                                }
                            }

                            if (!isDriveLetter && colonIndex + 1 < text.Length)
                            {
                                potentialPath = text.Substring(colonIndex + 1).Trim();
                            }
                        }

                        if (!string.IsNullOrEmpty(potentialPath))
                        {
                            string resolved = ResolveSpecialFolder(potentialPath);
                            if (Directory.Exists(resolved))
                            {
                                return resolved;
                            }
                        }

                        child = FileDialogNativeMethods.FindWindowEx(breadcrumbParent, child, "ToolbarWindow32", null);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileDialogNavigator] Failed to get dialog path: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
            return null;
        }
    }
}
