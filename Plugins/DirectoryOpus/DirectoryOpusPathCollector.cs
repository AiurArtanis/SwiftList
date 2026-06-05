using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk;

using System.Collections.Generic;

namespace SwiftList.Plugins.DirectoryOpus
{
    public class DirectoryOpusPathCollector : IActivePathCollector
    {
        public string Name => "Directory Opus";

        public string TargetName => "Directory Opus";

        public bool CanHandle(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<IntPtr, IntPtr> _lastActiveContainers = new Dictionary<IntPtr, IntPtr>();

        public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
        {
            if (windowHwnd == IntPtr.Zero) return null;

            IntPtr activeContainer = IntPtr.Zero;

            // 1. If currently focused control is inside a pane container, update our tracker
            if (activeHwnd != IntPtr.Zero)
            {
                activeContainer = GetAncestorOfClass(activeHwnd, "dopus.filedisplaycontainer");
                if (activeContainer != IntPtr.Zero)
                {
                    lock (_lastActiveContainers)
                    {
                        _lastActiveContainers[windowHwnd] = activeContainer;
                    }
                }
            }

            // 2. If focus is elsewhere (e.g. folder tree), retrieve the last active container for this Lister window
            if (activeContainer == IntPtr.Zero)
            {
                lock (_lastActiveContainers)
                {
                    _lastActiveContainers.TryGetValue(windowHwnd, out activeContainer);
                }
            }

            // 3. Fallback: If no history exists or the tracked window is no longer valid/visible, find the first visible container
            if (activeContainer == IntPtr.Zero || !IsWindow(activeContainer) || !IsWindowVisible(activeContainer))
            {
                activeContainer = FindFirstVisibleContainer(windowHwnd);
                lock (_lastActiveContainers)
                {
                    if (activeContainer != IntPtr.Zero)
                    {
                        _lastActiveContainers[windowHwnd] = activeContainer;
                    }

                    // Clean up closed Lister windows to prevent leaks
                    List<IntPtr>? deadKeys = null;
                    foreach (var key in _lastActiveContainers.Keys)
                    {
                        if (!IsWindow(key))
                        {
                            deadKeys ??= new List<IntPtr>();
                            deadKeys.Add(key);
                        }
                    }
                    if (deadKeys != null)
                    {
                        foreach (var key in deadKeys)
                        {
                            _lastActiveContainers.Remove(key);
                        }
                    }
                }
            }

            if (activeContainer != IntPtr.Zero)
            {
                // Try to get path from the Edit control under the active container
                IntPtr editWnd = FindWindowExRecursively(activeContainer, IntPtr.Zero, "Edit", null);
                if (editWnd != IntPtr.Zero)
                {
                    string path = GetWindowText(editWnd);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }

                // Try to get path from dopus.ctl.treepath under the active container
                IntPtr locationBar = FindWindowExRecursively(activeContainer, IntPtr.Zero, "dopus.ctl.treepath", null);
                if (locationBar != IntPtr.Zero)
                {
                    string path = GetWindowText(locationBar);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }

                // Fallback to active container's text
                {
                    string path = GetWindowText(activeContainer);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }

            // 4. Global fallback if everything else fails:
            // Try to find the first address bar Edit control in the window
            IntPtr globalAddressBarEdit = GetAddressBar(windowHwnd);
            if (globalAddressBarEdit != IntPtr.Zero)
            {
                string path = GetWindowText(globalAddressBarEdit);
                if (!string.IsNullOrEmpty(path))
                {
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }

            // Try to find the first dopus.ctl.treepath in the window
            IntPtr globalLocationBar = FindWindowExRecursively(windowHwnd, IntPtr.Zero, "dopus.ctl.treepath", null);
            if (globalLocationBar != IntPtr.Zero)
            {
                string path = GetWindowText(globalLocationBar);
                if (!string.IsNullOrEmpty(path))
                {
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }

            return null;
        }

        private static IntPtr FindFirstVisibleContainer(IntPtr listerHwnd)
        {
            IntPtr container = IntPtr.Zero;
            EnumChildWindows(listerHwnd, (hWnd, lParam) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                if (sb.ToString().Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsWindowVisible(hWnd))
                    {
                        container = hWnd;
                        return false; // Stop parent enum
                    }
                }
                return true; // Continue parent enum
            }, IntPtr.Zero);
            return container;
        }

        private static IntPtr GetAncestorOfClass(IntPtr hwnd, string targetClassName)
        {
            IntPtr current = hwnd;
            var sb = new StringBuilder(256);
            while (current != IntPtr.Zero)
            {
                GetClassName(current, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls.Equals(targetClassName, StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }
                if (cls.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = GetParent(current);
            }
            return IntPtr.Zero;
        }

        private static IntPtr GetAddressBar(IntPtr parent)
        {
            IntPtr locationBar = FindWindowExRecursively(parent, IntPtr.Zero, "dopus.ctl.treepath", null);
            if (locationBar != IntPtr.Zero)
            {
                return FindWindowExRecursively(locationBar, IntPtr.Zero, "Edit", null);
            }
            return IntPtr.Zero;
        }

        #region Win32 API Helpers
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        private const int GWL_STYLE = -16;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const uint WM_GETTEXT = 0x000D;

        private static string GetWindowText(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            SendMessage(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString().Trim();
        }

        private static IntPtr FindWindowExRecursively(IntPtr parent, IntPtr childAfter, string className, string? windowName)
        {
            IntPtr child = FindWindowEx(parent, childAfter, className, windowName);
            if (child != IntPtr.Zero) return child;

            child = FindWindowEx(parent, IntPtr.Zero, null, null);
            while (child != IntPtr.Zero)
            {
                IntPtr result = FindWindowExRecursively(child, IntPtr.Zero, className, windowName);
                if (result != IntPtr.Zero) return result;
                child = FindWindowEx(parent, child, null, null);
            }

            return IntPtr.Zero;
        }
        #endregion
    }
}
