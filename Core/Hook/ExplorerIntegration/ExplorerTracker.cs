using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Registries;
namespace SwiftList.Core.Hook;
public class ExplorerTracker : IDisposable
{
    private ExplorerNativeHooks.WinEventDelegate? _winEventDelegate;
    private IntPtr _hForegroundHook = IntPtr.Zero;
    private IntPtr _hNameChangeHook = IntPtr.Zero;
    private IntPtr _hLocationChangeHook = IntPtr.Zero;
    private IntPtr _hFocusHook = IntPtr.Zero;
    private bool _isRunning;
    private readonly FileDialogNavigationTracker _dialogTracker = new();
    private readonly ExplorerWindowClassifier _classifier;
    // Internal state exposed to ExplorerWindowClassifier
    public string? LastPath { get; set; }
    public IntPtr LastActiveHwnd { get; set; }
    public string? LastActiveExplorerPath => _dialogTracker.LastActiveExplorerPath;
    public string? LastActiveExplorerClassName { get; set; }
    public bool IsExplorerOrDesktopActive { get; set; }
    public bool IsDesktop { get; set; }
    private bool _isActiveWindowDialog;
    public bool IsActiveWindowDialog { get => _isActiveWindowDialog; set => _isActiveWindowDialog = value; }
    public bool IsActiveWindowExplorer { get; set; }
    public IFileDialogAdapter? ActiveAdapter { get; private set; }
    public IInlineSearchAdapter? ActiveInlineAdapter { get; private set; }
    private IntPtr _activeHwnd;
    public IntPtr ActiveHwnd
    {
        get => _activeHwnd;
        set
        {
            _activeHwnd = value;
            if (_activeHwnd != IntPtr.Zero)
            {
                var sbClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(_activeHwnd, sbClass, sbClass.Capacity);
                var className = sbClass.ToString();
                var processName = GetProcessName(_activeHwnd);
                ActiveAdapter = FileDialogAdapterRegistry.GetMatchingAdapter(_activeHwnd, className, processName);
                _isActiveWindowDialog = ActiveAdapter != null;
                ActiveInlineAdapter = InlineSearchAdapterRegistry.GetMatchingAdapter(_activeHwnd, className, processName);
            }
            else
            {
                ActiveAdapter = null;
                _isActiveWindowDialog = false;
                ActiveInlineAdapter = null;
            }
        }
    }
    public void SetActiveInlineAdapterDirectly(IInlineSearchAdapter? adapter, IntPtr hwnd)
    {
        ActiveInlineAdapter = adapter;
        _activeHwnd = hwnd;
        IsExplorerOrDesktopActive = adapter != null;
        if (adapter != null && hwnd != IntPtr.Zero)
        {
            var windowTitle = new StringBuilder(256);
            ExplorerNativeHooks.GetWindowText(hwnd, windowTitle, windowTitle.Capacity);
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
            RaiseExplorerActivated(hwnd, windowTitle.ToString(), sbClass.ToString(), false);
        }
    }
    public string? ActivePath => LastPath;
    public uint AppProcessId { get; set; }
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool>? OnPathCaptured;
    public event Action? OnActiveWindowMoved;
    public event Action<string>? OnError;
    internal string GetProcessName(IntPtr hwnd)
    {
        try
        {
            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0) return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch { }
        return "Unknown";
    }
    public void UpdateActiveWindow(IntPtr hwnd, string title, string className, bool isDesktop)
    {
        ActiveHwnd = hwnd;
        IsExplorerOrDesktopActive = true;
        IsDesktop = isDesktop;
        IsActiveWindowExplorer = className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase);
        if (!IsActiveWindowDialog) LastActiveExplorerClassName = className;
        RaiseExplorerActivated(hwnd, title, className, isDesktop);
    }
    public void DeactivateWindow() => Deactivate();
    public void UpdatePath(string path, bool isDesktop)
    {
        LastPath = path;
        Logger.Log($"[ExplorerTracker] UpdatePath captured path: {path} (isDesktop={isDesktop})", LogLevel.Debug);
        if (!IsActiveWindowDialog) _dialogTracker.SetLastActiveExplorerPath(path);
        RaisePathCaptured(path, isDesktop);
    }
    public void MoveActiveWindow() => OnActiveWindowMoved?.Invoke();
    public void RaiseErrorExternal(string msg) => RaiseError(msg);
    internal void RaiseExplorerActivated(IntPtr hwnd, string title, string cls, bool isDesktop) => OnExplorerActivated?.Invoke(hwnd, title, cls, isDesktop);
    internal void RaisePathCaptured(string path, bool isDesktop) => OnPathCaptured?.Invoke(path, isDesktop);
    internal void RaiseError(string msg) => OnError?.Invoke(msg);
    public ExplorerTracker() => _classifier = new ExplorerWindowClassifier(this, _dialogTracker);
    public void Start()
    {
        if (_isRunning) return;
        _winEventDelegate = new ExplorerNativeHooks.WinEventDelegate(WinEventProc);
        _hForegroundHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hNameChangeHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE, ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hLocationChangeHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE, ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        _hFocusHook = ExplorerNativeHooks.SetWinEventHook(
            ExplorerNativeHooks.EVENT_OBJECT_FOCUS, ExplorerNativeHooks.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventDelegate, 0, 0, ExplorerNativeHooks.WINEVENT_OUTOFCONTEXT);
        if (_hForegroundHook == IntPtr.Zero || _hNameChangeHook == IntPtr.Zero || _hLocationChangeHook == IntPtr.Zero || _hFocusHook == IntPtr.Zero)
        {
            Stop();
            Logger.Log("[ExplorerTracker] Failed to register WinEvent hooks!", LogLevel.Error);
            return;
        }
        _isRunning = true;
        Logger.Log("[ExplorerTracker] Started.");
        _classifier.CheckActiveWindow(ExplorerNativeHooks.GetForegroundWindow());
    }
    public void Stop()
    {
        if (_hForegroundHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hForegroundHook); _hForegroundHook = IntPtr.Zero; }
        if (_hNameChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hNameChangeHook); _hNameChangeHook = IntPtr.Zero; }
        if (_hLocationChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hLocationChangeHook); _hLocationChangeHook = IntPtr.Zero; }
        if (_hFocusHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hFocusHook); _hFocusHook = IntPtr.Zero; }
        _winEventDelegate = null;
        _isRunning = false;
        LastPath = null;
        LastActiveHwnd = IntPtr.Zero;
        IsExplorerOrDesktopActive = false;
        IsDesktop = false;
        ActiveHwnd = IntPtr.Zero;
        _dialogTracker.Clear();
        Logger.Log("[ExplorerTracker] Stopped.");
    }
    public bool TryGetActiveWindowRect(out RECT rect)
    {
        rect = default;
        if (ActiveHwnd == IntPtr.Zero) return false;
        if (ActiveAdapter != null && ActiveAdapter.GetDockBounds(ActiveHwnd, out var r1))
        {
            rect = new RECT { Left = r1.Left, Top = r1.Top, Right = r1.Right, Bottom = r1.Bottom };
            return true;
        }
        if (ActiveInlineAdapter != null && ActiveInlineAdapter.GetDockBounds(ActiveHwnd, out var r2))
        {
            rect = new RECT { Left = r2.Left, Top = r2.Top, Right = r2.Right, Bottom = r2.Bottom };
            return true;
        }
        var nativeRect = new ExplorerNativeHooks.RECT();
        if (ExplorerNativeHooks.DwmGetWindowAttribute(ActiveHwnd, ExplorerNativeHooks.DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, System.Runtime.InteropServices.Marshal.SizeOf<ExplorerNativeHooks.RECT>()) == 0 ||
            ExplorerNativeHooks.GetWindowRect(ActiveHwnd, out nativeRect))
        {
            rect = new RECT { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning || hwnd == IntPtr.Zero) return;
        if (idObject != 0) return;
        if (eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND)
        {
            _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE)
        {
            if (hwnd == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (hwnd == ActiveHwnd && IsActiveWindowDialog)
                OnActiveWindowMoved?.Invoke();
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_FOCUS)
        {
            var root = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (root == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(root);
        }
        var currentFg = ExplorerNativeHooks.GetForegroundWindow();
        if (currentFg != IntPtr.Zero && currentFg != ActiveHwnd)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(currentFg, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();
            var processName = GetProcessName(currentFg);
            if (FileDialogAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null ||
                InlineSearchAdapterRegistry.GetMatchingAdapter(currentFg, className, processName) != null)
            {
                _classifier.CheckActiveWindow(currentFg);
            }
        }
        if (IsActiveWindowDialog && ActiveHwnd != IntPtr.Zero && ActiveAdapter != null)
        {
            var activePath = ActiveAdapter.GetCurrentPath(ActiveHwnd);
            if (!string.IsNullOrEmpty(activePath) && activePath != LastPath)
            {
                UpdatePath(activePath, false);
            }
        }

        var polledByCollector = false;
        if (ActiveHwnd != IntPtr.Zero && ActiveInlineAdapter == null)
        {
            var sbClass = new StringBuilder(256);
            ExplorerNativeHooks.GetClassName(ActiveHwnd, sbClass, sbClass.Capacity);
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
                        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(ActiveHwnd, out _);
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

                    if (focused == IntPtr.Zero) focused = ActiveHwnd;

                    var activePath = collector.TryGetPath(focused, activeClassName, ActiveHwnd, activeClass, GetProcessName(ActiveHwnd));
                    if (!string.IsNullOrEmpty(activePath))
                    {
                        if (activePath != LastPath)
                        {
                            UpdatePath(activePath, false);
                        }
                    }
                    else if (!string.IsNullOrEmpty(LastPath))
                    {
                        UpdatePath(string.Empty, false);
                    }
                    break;
                }
            }
        }

        if (!polledByCollector && ActiveInlineAdapter != null && ActiveHwnd != IntPtr.Zero)
        {
            var activePath = ActiveInlineAdapter.GetSearchScope(ActiveHwnd);
            if (!string.IsNullOrEmpty(activePath))
            {
                if (activePath != LastPath)
                {
                    UpdatePath(activePath, false);
                }
            }
            else if (!string.IsNullOrEmpty(LastPath))
            {
                UpdatePath(string.Empty, false);
            }
        }
    }
    internal void Deactivate()
    {
        var wasActive = IsExplorerOrDesktopActive;
        IsExplorerOrDesktopActive = IsDesktop = IsActiveWindowDialog = IsActiveWindowExplorer = false;
        ActiveHwnd = LastActiveHwnd = IntPtr.Zero;
        LastPath = null;
        if (wasActive) OnExplorerDeactivated?.Invoke();
    }
    public void Dispose() => Stop();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    public static IntPtr FindSubEditBox(IntPtr parent) => ExplorerNativeHooks.FindSubEditBox(parent);
}
