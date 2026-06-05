using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Plugins.DirectoryOpus.Win32
{
    public static class Win32Helper
    {
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint WM_GETTEXT = 0x000D;

        public static string GetWindowText(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            SendMessage(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString().Trim();
        }

        public static IntPtr FindWindowExRecursively(IntPtr parent, IntPtr childAfter, string className, string? windowName)
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

        public static IntPtr FindReplacementContainer(IntPtr listerHwnd, RECT targetRect)
        {
            IntPtr bestContainer = IntPtr.Zero;
            int minDistance = int.MaxValue;

            EnumChildWindows(listerHwnd, (hWnd, lParam) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                if (sb.ToString().Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsWindowVisible(hWnd))
                    {
                        if (GetWindowRect(hWnd, out RECT rect))
                        {
                            int dx = rect.Left - targetRect.Left;
                            int dy = rect.Top - targetRect.Top;
                            int distance = dx * dx + dy * dy;
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                bestContainer = hWnd;
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return bestContainer;
        }

        public static IntPtr FindFirstVisibleContainer(IntPtr listerHwnd)
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

        public static IntPtr GetAncestorOfClass(IntPtr hwnd, string targetClassName)
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

        public static IntPtr GetAddressBar(IntPtr parent)
        {
            IntPtr locationBar = FindWindowExRecursively(parent, IntPtr.Zero, "dopus.ctl.treepath", null);
            if (locationBar != IntPtr.Zero)
            {
                return FindWindowExRecursively(locationBar, IntPtr.Zero, "Edit", null);
            }
            return IntPtr.Zero;
        }

        public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect)
        {
            return GetWindowRect(hWnd, out rect);
        }
    }
}
