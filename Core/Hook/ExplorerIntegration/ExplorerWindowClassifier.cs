using System;
using System.Text;
using System.Threading;
using SwiftList.Core;

namespace SwiftList.Core.Hook
{
    /// <summary>
    /// Handles window classification and path tracking for ExplorerTracker,
    /// delegating path collection to registered IActivePathCollector plugins.
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

                bool isDesktop = ExplorerNativeHooks.IsDesktopWindow(rootHwnd, out string windowClassName);
                Logger.Log($"[ExplorerTracker] Active window: HWND=0x{hwnd:X}, Root=0x{rootHwnd:X}, Class={windowClassName}, isDesktop={isDesktop}", LogLevel.Debug);

                // Resolve the actual focused control handle inside the active window's thread
                IntPtr focusedHwnd = IntPtr.Zero;
                string activeClassName = string.Empty;
                try
                {
                    uint threadId = KeyboardNativeMethods.GetWindowThreadProcessId(rootHwnd, out _);
                    var guiInfo = new KeyboardNativeMethods.GUITHREADINFO();
                    guiInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(guiInfo);
                    if (KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
                    {
                        focusedHwnd = guiInfo.hwndFocus;
                        var sbActiveCls = new StringBuilder(256);
                        KeyboardNativeMethods.GetClassName(focusedHwnd, sbActiveCls, sbActiveCls.Capacity);
                        activeClassName = sbActiveCls.ToString();
                    }
                }
                catch { }

                if (focusedHwnd == IntPtr.Zero)
                {
                    focusedHwnd = hwnd;
                    var sbActiveCls = new StringBuilder(256);
                    ExplorerNativeHooks.GetClassName(hwnd, sbActiveCls, sbActiveCls.Capacity);
                    activeClassName = sbActiveCls.ToString();
                }

                // Get process name of root window
                string processName = "Unknown";
                try
                {
                    ExplorerNativeHooks.GetWindowThreadProcessId(rootHwnd, out uint pid);
                    if (pid != 0)
                    {
                        using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                        {
                            processName = proc.ProcessName;
                        }
                    }
                }
                catch { }

                // Delegate active path collection to registered plugins
                var collectors = SwiftList.PluginSdk.ActivePathCollectorRegistry.GetCollectors();
                bool handledByPlugin = false;

                foreach (var collector in collectors)
                {
                    try
                    {
                        if (collector.CanHandle(windowClassName))
                        {
                            string? activePath = collector.TryGetPath(focusedHwnd, activeClassName, rootHwnd, windowClassName, processName);
                            if (!string.IsNullOrEmpty(activePath))
                            {
                                handledByPlugin = true;
                                _tracker.IsExplorerOrDesktopActive = true;
                                _tracker.IsDesktop = isDesktop;
                                _tracker.IsActiveWindowDialog = false;
                                _tracker.IsActiveWindowExplorer = !isDesktop && windowClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
                                _tracker.LastActiveExplorerClassName = windowClassName;
                                _tracker.ActiveHwnd = rootHwnd;

                                if (rootHwnd != _tracker.LastActiveHwnd)
                                {
                                    _tracker.LastActiveHwnd = rootHwnd;
                                    var windowTitle = new StringBuilder(256);
                                    ExplorerNativeHooks.GetWindowText(rootHwnd, windowTitle, windowTitle.Capacity);
                                    _tracker.RaiseExplorerActivated(rootHwnd, windowTitle.ToString(), windowClassName, isDesktop);
                                }

                                if (_dialogTracker.LastActiveExplorerPath != activePath)
                                    _dialogTracker.SetLastActiveExplorerPath(activePath);

                                if (activePath != _tracker.LastPath)
                                {
                                    _tracker.LastPath = activePath;
                                    _tracker.RaisePathCaptured(activePath, isDesktop);
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ExplorerTracker] Error invoking active path collector '{collector.Name}': {ex.Message}", LogLevel.Error);
                    }
                }

                if (handledByPlugin)
                {
                    return;
                }

                if (windowClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase))
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
    }
}
