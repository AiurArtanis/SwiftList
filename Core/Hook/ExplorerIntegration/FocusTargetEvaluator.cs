using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Core.Hook
{
    internal static class FocusTargetEvaluator
    {
        public static bool IsForegroundTextInputFocused(IntPtr foregroundHwnd)
        {
            uint threadId = KeyboardNativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            if (threadId == 0)
                return false;

            var info = new KeyboardNativeMethods.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
            };

            if (!KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
                return false;

            var className = new StringBuilder(128);
            if (KeyboardNativeMethods.GetClassName(info.hwndFocus, className, className.Capacity) == 0)
                return false;

            string cls = className.ToString();
            if (cls.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("TextBox", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HasActiveCaret(foregroundHwnd, info);
        }

        private static bool HasActiveCaret(IntPtr foregroundHwnd, KeyboardNativeMethods.GUITHREADINFO info)
        {
            if (info.hwndCaret == IntPtr.Zero)
                return false;

            if (info.rcCaret.Right <= info.rcCaret.Left && info.rcCaret.Bottom <= info.rcCaret.Top)
                return false;

            return info.hwndCaret == foregroundHwnd || KeyboardNativeMethods.IsChild(foregroundHwnd, info.hwndCaret);
        }

        public static bool IsExplorerFileViewFocused(IntPtr foregroundHwnd)
        {
            uint threadId = KeyboardNativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            if (threadId == 0)
                return false;

            var info = new KeyboardNativeMethods.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
            };

            if (!KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
                return false;

            IntPtr current = info.hwndFocus;
            for (int depth = 0; depth < 12 && current != IntPtr.Zero; depth++)
            {
                string cls = GetWindowClassName(current);
                if (cls.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (current == foregroundHwnd)
                    break;

                current = KeyboardNativeMethods.GetParent(current);
            }

            return false;
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            var className = new StringBuilder(128);
            return KeyboardNativeMethods.GetClassName(hwnd, className, className.Capacity) == 0
                ? string.Empty
                : className.ToString();
        }
    }
}
