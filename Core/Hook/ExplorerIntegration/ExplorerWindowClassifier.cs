using System;
using System.Text;
using System.Threading;
using SwiftList.Core;

namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Handles window classification and path tracking for ExplorerTracker,
    /// extracting the CheckActiveWindow dispatch and all per-window-class tracking methods.
    /// </summary>
    internal sealed class ExplorerWindowClassifier
    {
        private readonly ExplorerTracker _tracker;
        private readonly ExplorerDialogNavigationTracker _dialogTracker;

        public ExplorerWindowClassifier(ExplorerTracker tracker, ExplorerDialogNavigationTracker dialogTracker)
        {
            _tracker = tracker;
            _dialogTracker = dialogTracker;
        }

        public void CheckActiveWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                if (IsFocusChangeIgnored(hwnd))
                    return;

                IntPtr mainDialog = ExplorerNativeHooks.FindMainFileDialog(hwnd);
                if (mainDialog != IntPtr.Zero && ExplorerNativeHooks.HasBreadcrumbParent(mainDialog))
                {
                    IntPtr targetEdit = ExplorerNativeHooks.FindSubEditBox(mainDialog);
                    if (targetEdit != IntPtr.Zero)
                    {
                        TrackFileDialogWindow(mainDialog, targetEdit);
                        return;
                    }
                }

                IntPtr rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
                if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

                bool isDesktop = ExplorerNativeHooks.IsDesktopWindow(rootHwnd, out string clsName);
                Logger.Log($"[ExplorerTracker] Active window: HWND=0x{hwnd:X}, Root=0x{rootHwnd:X}, Class={clsName}, isDesktop={isDesktop}", LogLevel.Debug);

                if (isDesktop)
                    TrackDesktopWindow(rootHwnd, clsName);
                else if (clsName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase))
                    TrackCabinetWindow(rootHwnd, clsName);
                else if (clsName.Equals("#32770", StringComparison.OrdinalIgnoreCase))
                {
                    _tracker.IsExplorerOrDesktopActive = true;
                    _tracker.IsDesktop = false;
                    _tracker.IsActiveWindowDialog = true;
                    _tracker.IsActiveWindowExplorer = false;
                    _tracker.ActiveHwnd = rootHwnd;
                }
                else
                {
                    _tracker.Deactivate();
                }
            }
            catch (Exception ex)
            {
                _tracker.RaiseError(ex.Message);
            }
        }

        private bool IsFocusChangeIgnored(IntPtr hwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
            if (sbClass.ToString().Contains("InputSwitch", StringComparison.OrdinalIgnoreCase))
                return true;

            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out uint activePid);
            if (activePid == Environment.ProcessId || (activePid != 0 && activePid == _tracker.AppProcessId))
                return true;

            return false;
        }

        private void TrackFileDialogWindow(IntPtr mainDialog, IntPtr targetEdit)
        {
            _dialogTracker.HandleDialogSeen(mainDialog, targetEdit);

            _tracker.IsExplorerOrDesktopActive = true;
            _tracker.IsDesktop = false;
            _tracker.IsActiveWindowDialog = true;
            _tracker.ActiveHwnd = mainDialog;

            string? activePath = FileDialogNavigator.GetDialogFolderPath(mainDialog);
            _tracker.LastPath = !string.IsNullOrEmpty(activePath) ? activePath : string.Empty;

            var windowTitle = new StringBuilder(256);
            ExplorerNativeHooks.GetWindowText(mainDialog, windowTitle, windowTitle.Capacity);

            StringBuilder sbCls2 = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(mainDialog, sbCls2, sbCls2.Capacity);

            if (mainDialog != _tracker.LastActiveHwnd)
            {
                _tracker.LastActiveHwnd = mainDialog;
                _tracker.RaiseExplorerActivated(mainDialog, windowTitle.ToString(), sbCls2.ToString(), false);
            }

            _tracker.RaisePathCaptured(_tracker.LastPath, false);
        }

        private void TrackDesktopWindow(IntPtr rootHwnd, string clsName)
        {
            _tracker.IsExplorerOrDesktopActive = true;
            _tracker.IsDesktop = true;
            _tracker.IsActiveWindowDialog = false;
            _tracker.IsActiveWindowExplorer = false;
            _tracker.ActiveHwnd = rootHwnd;

            if (rootHwnd != _tracker.LastActiveHwnd)
            {
                _tracker.LastActiveHwnd = rootHwnd;
                _tracker.RaiseExplorerActivated(rootHwnd, "Desktop", clsName, true);

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (_dialogTracker.LastActiveExplorerPath != desktopPath)
                        _dialogTracker.SetLastActiveExplorerPath(desktopPath);
                    if (desktopPath != _tracker.LastPath)
                    {
                        _tracker.LastPath = desktopPath;
                        _tracker.RaisePathCaptured(desktopPath, true);
                    }
                });
            }
        }

        private void TrackCabinetWindow(IntPtr rootHwnd, string clsName)
        {
            _tracker.IsExplorerOrDesktopActive = true;
            _tracker.IsDesktop = false;
            _tracker.IsActiveWindowDialog = false;
            _tracker.IsActiveWindowExplorer = true;
            _tracker.ActiveHwnd = rootHwnd;

            var windowTitle = new StringBuilder(256);
            ExplorerNativeHooks.GetWindowText(rootHwnd, windowTitle, windowTitle.Capacity);

            if (rootHwnd != _tracker.LastActiveHwnd)
            {
                _tracker.LastActiveHwnd = rootHwnd;
                _tracker.RaiseExplorerActivated(rootHwnd, windowTitle.ToString(), clsName, false);
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? activePath = ExplorerComNavigator.GetActiveExplorerPath(rootHwnd, msg => _tracker.RaiseError(msg));
                if (!string.IsNullOrEmpty(activePath))
                {
                    if (_dialogTracker.LastActiveExplorerPath != activePath)
                        _dialogTracker.SetLastActiveExplorerPath(activePath);
                    if (activePath != _tracker.LastPath)
                    {
                        _tracker.LastPath = activePath;
                        _tracker.RaisePathCaptured(activePath, false);
                    }
                }
            });
        }
    }
}
