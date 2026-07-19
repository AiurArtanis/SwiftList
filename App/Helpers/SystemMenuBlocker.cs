using System.Windows;
using System.Windows.Interop;

namespace SwiftList.App.Helpers;

/// <summary>
/// Blocks two OS-level WM_SYSCOMMAND triggers on these custom-chrome windows, neither of which has a
/// legitimate place here: Alt+Space (SC_KEYMENU), which pops up the OS-drawn system menu (Restore/Move/
/// Size/Minimize/Maximize/Close) as a jarring blank box clipped by the window's own borderless/rounded
/// corners instead of a real title bar; and Alt+F4 (SC_CLOSE), which would otherwise let the OS close
/// these windows out from under the app's own show/hide lifecycle (e.g. the quick window is only ever
/// meant to Hide(), never actually Close()) -- every other WM_SYSCOMMAND subcommand is left untouched.
/// Attached to the inline window too, but doesn't reliably cover it there: unresolved, not pursued further.
/// </summary>
public static class SystemMenuBlocker
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;
    private const int SC_CLOSE = 0xF060;

    public static void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            Hook(hwndSource);
        }
        else
        {
            window.SourceInitialized += (s, e) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource src)
                    Hook(src);
            };
        }
    }

    private static void Hook(HwndSource hwndSource) => hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
    {
        if (msg == WM_SYSCOMMAND)
        {
            var command = (int)wParam & 0xFFF0;
            if (command == SC_KEYMENU || command == SC_CLOSE)
                handled = true;
        }
        return IntPtr.Zero;
    });
}
