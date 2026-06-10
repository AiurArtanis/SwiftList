using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace SwiftList.Tutorial.Helpers;

public static class Win32Helper
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static string GetForegroundSearchText(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return "";

            var editElement = element.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            if (editElement != null)
            {
                if (editElement.GetCurrentPattern(ValuePattern.Pattern) is ValuePattern valuePattern)
                {
                    return valuePattern.Current.Value ?? "";
                }
            }
        }
        catch { }
        return "";
    }

    public static bool IsActionsMenuOpen(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return false;

            var listElement = element.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));

            return listElement != null;
        }
        catch { }
        return false;
    }

    public static string GetProcessNameFromWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        GetWindowThreadProcessId(hWnd, out var pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
