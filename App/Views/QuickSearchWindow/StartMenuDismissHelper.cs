using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.Core;

namespace SwiftList.App.Views.QuickSearchWindow;

internal static class StartMenuDismissHelper
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly string[] ShellSearchHostProcessNames = { "SearchHost", "StartMenuExperienceHost" };

    public static string TryGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "?"; }
    }

    private static bool IsStartMenuFocused()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(fgHwnd, out var fgPid);
        var fgProcessName = TryGetProcessName(fgPid);

        if (ShellSearchHostProcessNames.Any(p => string.Equals(p, fgProcessName, StringComparison.OrdinalIgnoreCase)))
            return true;

        var sbClass = new StringBuilder(256);
        GetClassName(fgHwnd, sbClass, sbClass.Capacity);
        var fgClassName = sbClass.ToString();

        if (fgClassName == "Windows.UI.Core.CoreWindow" &&
            (fgProcessName.Contains("SearchHost", StringComparison.OrdinalIgnoreCase) ||
             fgProcessName.Contains("StartMenu", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static void DismissStartMenuIfOpen()
    {
        for (var i = 0; i < 3; i++)
        {
            if (!IsStartMenuFocused())
                return;

            Logger.Log($"[StartMenuDismissHelper] Start Menu detected open -- dismissing (attempt {i + 1})", LogLevel.Debug);
            keybd_event(VK_ESCAPE, 0, 0, IntPtr.Zero);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

            // Wait up to 100ms (in 20ms steps) for the focus to transition away from the Start Menu
            for (var j = 0; j < 5; j++)
            {
                Thread.Sleep(20);
                if (!IsStartMenuFocused())
                    break;
            }
        }
    }
}
