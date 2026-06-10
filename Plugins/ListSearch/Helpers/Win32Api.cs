using System.Runtime.InteropServices;
using System.Text;

namespace SwiftList.Plugins.ListSearch.Helpers;

internal static class Win32Api
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    public const int GWL_STYLE = -16;
    public const int GWL_ID = -12;

    public const uint LBS_MULTIPLESEL = 0x0008;
    public const uint LBS_EXTENDEDSEL = 0x0800;
    public const uint LVS_SINGLESEL = 0x0004;

    public const uint LB_GETCOUNT = 0x018B;
    public const uint LB_GETTEXTLEN = 0x018A;
    public const uint LB_GETTEXT = 0x0189;
    public const uint LB_SETCURSEL = 0x0186;
    public const uint LB_SETSEL = 0x0185;
    public const uint LB_SETCARETINDEX = 0x019E;
    public const uint LB_GETSEL = 0x0187;
    public const uint LB_GETCURSEL = 0x0188;

    public const uint LVM_GETNEXTITEM = 0x100C;
    public const uint LVNI_SELECTED = 0x0002;

    public const uint WM_COMMAND = 0x0111;
    public const uint LBN_SELCHANGE = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }
}
