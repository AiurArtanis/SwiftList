using System;
using System.IO;
using System.Collections.Generic;
using SwiftList.PluginSdk;
using SwiftList.Plugins.DirectoryOpus.Win32;

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
                activeContainer = Win32Helper.GetAncestorOfClass(activeHwnd, "dopus.filedisplaycontainer");
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

            // 3. Fallback: If no history exists or the tracked window is no longer valid/visible, find a replacement visible container
            if (activeContainer == IntPtr.Zero || !Win32Helper.IsWindow(activeContainer))
            {
                activeContainer = Win32Helper.FindFirstVisibleContainer(windowHwnd);
                lock (_lastActiveContainers)
                {
                    if (activeContainer != IntPtr.Zero)
                    {
                        _lastActiveContainers[windowHwnd] = activeContainer;
                    }
                    CleanUpDeadKeys();
                }
            }
            else if (!Win32Helper.IsWindowVisible(activeContainer))
            {
                // The tracked container is hidden (e.g. user switched tabs in this pane while focus was in the tree).
                // Find the new visible container occupying the same position/pane.
                if (Win32Helper.TryGetWindowRect(activeContainer, out Win32Helper.RECT oldRect))
                {
                    IntPtr replacement = Win32Helper.FindReplacementContainer(windowHwnd, oldRect);
                    if (replacement != IntPtr.Zero)
                        activeContainer = replacement;
                }

                lock (_lastActiveContainers)
                {
                    _lastActiveContainers[windowHwnd] = activeContainer;
                    CleanUpDeadKeys();
                }
            }

            if (activeContainer != IntPtr.Zero)
            {
                // Try to get path from the Edit control under the active container
                IntPtr editWnd = Win32Helper.FindWindowExRecursively(activeContainer, IntPtr.Zero, "Edit", null);
                if (editWnd != IntPtr.Zero)
                {
                    string path = Win32Helper.GetWindowText(editWnd);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }

                // Try to get path from dopus.ctl.treepath under the active container
                IntPtr locationBar = Win32Helper.FindWindowExRecursively(activeContainer, IntPtr.Zero, "dopus.ctl.treepath", null);
                if (locationBar != IntPtr.Zero)
                {
                    string path = Win32Helper.GetWindowText(locationBar);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }

                // Fallback to active container's text
                {
                    string path = Win32Helper.GetWindowText(activeContainer);
                    string resolved = ShellPathHelper.ResolveSpecialFolder(path);
                    if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }

            // 4. Global fallback if everything else fails:
            // Try to find the first address bar Edit control in the window
            IntPtr globalAddressBarEdit = Win32Helper.GetAddressBar(windowHwnd);
            if (globalAddressBarEdit != IntPtr.Zero)
            {
                string path = Win32Helper.GetWindowText(globalAddressBarEdit);
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
            IntPtr globalLocationBar = Win32Helper.FindWindowExRecursively(windowHwnd, IntPtr.Zero, "dopus.ctl.treepath", null);
            if (globalLocationBar != IntPtr.Zero)
            {
                string path = Win32Helper.GetWindowText(globalLocationBar);
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

        private static void CleanUpDeadKeys()
        {
            List<IntPtr>? deadKeys = null;
            foreach (var key in _lastActiveContainers.Keys)
            {
                if (!Win32Helper.IsWindow(key))
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
}
