using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.FileDialog;

public class StandardFileDialogAdapter : IFileDialogAdapter
{
    public string Name => TranslationService.Get("Plugins_StandardFileDialogAdapterName");

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;
        return FindBreadcrumbParent(hwnd) != IntPtr.Zero;
    }

    public string? GetCurrentPath(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return null;
            var breadcrumbParent = FindBreadcrumbParent(hwnd);
            if (breadcrumbParent != IntPtr.Zero)
            {
                var child = FindWindowEx(breadcrumbParent, IntPtr.Zero, "ToolbarWindow32", null);
                while (child != IntPtr.Zero)
                {
                    var textSb = new StringBuilder(1024);
                    SendMessage(child, WM_GETTEXT, (IntPtr)textSb.Capacity, textSb);
                    var text = textSb.ToString().Trim();
                    var potentialPath = text;
                    var colonIndex = text.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        var isDriveLetter = colonIndex == 1 && text.Length >= 2 &&
                            ((text[0] >= 'a' && text[0] <= 'z') || (text[0] >= 'A' && text[0] <= 'Z'));
                        if (!isDriveLetter && colonIndex + 1 < text.Length)
                            potentialPath = text.Substring(colonIndex + 1).Trim();
                    }
                    if (!string.IsNullOrEmpty(potentialPath))
                    {
                        var resolved = ShellPathHelper.ResolveSpecialFolder(potentialPath);
                        if (Directory.Exists(resolved)) return resolved;
                    }
                    child = FindWindowEx(breadcrumbParent, child, "ToolbarWindow32", null);
                }
            }
        }
        catch { }
        return null;
    }

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        try
        {
            var targetEdit = FindSubEditBox(hwnd);
            if (targetEdit == IntPtr.Zero) return false;

            if (Directory.Exists(targetPath) && !targetPath.EndsWith("\\"))
                targetPath += "\\";

            var currentPath = GetCurrentPath(hwnd);
            if (currentPath != null && string.Equals(currentPath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return true;

            SendMessage(targetEdit, WM_SETTEXT, IntPtr.Zero, targetPath);
            var parent = GetParent(targetEdit);
            var ctrlId = GetDlgCtrlID(targetEdit);
            if (parent != IntPtr.Zero)
            {
                var wParamChange = (IntPtr)((EN_CHANGE << 16) | (uint)ctrlId);
                SendMessage(parent, WM_COMMAND, wParamChange, targetEdit);
            }

            Task.Run(async () =>
            {
                await Task.Delay(150);
                var currentActive = GetForegroundWindow();
                if (currentActive == hwnd)
                {
                    var targetThread = GetWindowThreadProcessId(targetEdit, out var _);
                    var currentThread = GetCurrentThreadId();
                    var attached = false;
                    try
                    {
                        if (targetThread != 0 && targetThread != currentThread)
                            attached = AttachThreadInput(currentThread, targetThread, true);

                        SetForegroundWindow(hwnd);
                        SetFocus(targetEdit);
                        PostMessage(targetEdit, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                        PostMessage(targetEdit, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
                        PostMessage(targetEdit, WM_LBUTTONDOWN, (IntPtr)1, IntPtr.Zero);
                        PostMessage(targetEdit, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                        PostMessage(targetEdit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    }
                    finally
                    {
                        if (attached) AttachThreadInput(currentThread, targetThread, false);
                    }
                }
            });
            return true;
        }
        catch { return false; }
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;
        var nativeRect = new RECT();
        var result = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<RECT>());
        if (result == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        if (GetWindowRect(hwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        try
        {
            var targetEdit = FindSubEditBox(hwnd);
            if (targetEdit == IntPtr.Zero) return false;
            var targetThread = GetWindowThreadProcessId(targetEdit, out var _);
            var currentThread = GetCurrentThreadId();
            var attached = false;
            try
            {
                if (targetThread != 0 && targetThread != currentThread)
                    attached = AttachThreadInput(currentThread, targetThread, true);

                SetForegroundWindow(hwnd);
                SetFocus(targetEdit);
                PostMessage(targetEdit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                return true;
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, targetThread, false);
            }
        }
        catch { return false; }
    }

    #region Win32 API Helpers
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_COMMAND = 0x0111;
    private const uint EN_CHANGE = 0x0300;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint EM_SETSEL = 0x00B1;
    private const int VK_RETURN = 0x0D;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static IntPtr FindBreadcrumbParent(IntPtr parent)
    {
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(child, classNameSb, classNameSb.Capacity);
            if (classNameSb.ToString().Equals("Breadcrumb Parent", StringComparison.OrdinalIgnoreCase))
                return child;
            var subParent = FindBreadcrumbParent(child);
            if (subParent != IntPtr.Zero) return subParent;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindSubEditBox(IntPtr parent)
    {
        var edit = FindWindowEx(parent, IntPtr.Zero, "Edit", null);
        if (edit != IntPtr.Zero) return edit;

        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var subEdit = FindSubEditBox(child);
            if (subEdit != IntPtr.Zero) return subEdit;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }
    #endregion
}
