using System.Text;
using SwiftList.PluginSdk.Registries;
using SwiftList.Core.Hook.InlineSearch;
namespace SwiftList.Core.Hook;

internal sealed class ExplorerActivePathPoller
{
    private readonly ExplorerWindowClassifier _classifier;
    public ExplorerActivePathPoller(ExplorerWindowClassifier classifier) => _classifier = classifier;
    public void Poll(ExplorerTracker tracker)
    {
        var currentFg = ExplorerNativeHooks.GetForegroundWindow();
        if (currentFg != IntPtr.Zero && currentFg != tracker.ActiveHwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(currentFg, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            var processName = tracker.GetProcessName(currentFg);
            if (FileDialogAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null ||
                InlineSearchAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null)
            {
                _classifier.CheckActiveWindow(currentFg);
            }
        }
        if (tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveAdapter != null)
        {
            var activePath = tracker.ActiveAdapter.GetCurrentPath(tracker.ActiveHwnd);
            if (!string.IsNullOrEmpty(activePath) && activePath != tracker.LastPath)
            {
                tracker.UpdatePath(activePath, false);
            }
        }

        var polledByCollector = false;
        if (tracker.ActiveHwnd != IntPtr.Zero && tracker.ActiveInlineAdapter == null)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(tracker.ActiveHwnd, sbClass, sbClass.Capacity);
            var activeClass = sbClass.ToString();
            var collectors = ActivePathCollectorRegistry.GetCollectors();
            foreach (var collector in collectors)
            {
                if (collector.CanHandle(activeClass))
                {
                    polledByCollector = true;
                    var focused = IntPtr.Zero;
                    var activeClassName = string.Empty;
                    try
                    {
                        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(tracker.ActiveHwnd, out _);
                        var guiInfo = new KeyboardNativeMethods.GUITHREADINFO();
                        guiInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(guiInfo);
                        if (KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
                        {
                            focused = guiInfo.hwndFocus;
                            var sbActiveCls = new StringBuilder(256);
                            KeyboardNativeMethods.GetClassName(focused, sbActiveCls, sbActiveCls.Capacity);
                            activeClassName = sbActiveCls.ToString();
                        }
                    }
                    catch { }

                    if (focused == IntPtr.Zero) focused = tracker.ActiveHwnd;

                    var activePath = collector.TryGetPath(focused, activeClassName, tracker.ActiveHwnd, activeClass, tracker.GetProcessName(tracker.ActiveHwnd));
                    if (!string.IsNullOrEmpty(activePath))
                    {
                        if (activePath != tracker.LastPath)
                        {
                            tracker.UpdatePath(activePath, false);
                        }
                    }
                    else if (!string.IsNullOrEmpty(tracker.LastPath))
                    {
                        tracker.UpdatePath(string.Empty, false);
                    }
                    break;
                }
            }
        }

        if (!polledByCollector && tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
        {
            var activePath = tracker.ActiveInlineAdapter.GetSearchScope(tracker.ActiveHwnd);
            if (!string.IsNullOrEmpty(activePath))
            {
                if (activePath != tracker.LastPath)
                {
                    tracker.UpdatePath(activePath, false);
                }
            }
            else if (!string.IsNullOrEmpty(tracker.LastPath))
            {
                tracker.UpdatePath(string.Empty, false);
            }
        }
    }
}
