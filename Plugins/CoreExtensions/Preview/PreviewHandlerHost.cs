using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Preview;

// Hosts a native Windows Preview Handler (IPreviewHandler) inside WPF via an HwndHost, so rich formats
// (PDF, Office, RTF, ...) render with their real system preview. Handlers are drawn from a session-scoped
// pool so navigating between files reuses them instead of re-spawning the prevhost surrogate; TrySetTarget
// re-points this same host at a new file without rebuilding the host window (or its overlay).
internal sealed class PreviewHandlerHost : HwndHost, IReusablePreview
{
    private readonly PreviewHandlerPool _pool;
    private IntPtr _hostHwnd;

    private string _targetPath;
    private Guid _targetClsid;
    private int _generation;

    private object? _activeComObject;
    private IPreviewHandler? _activeHandler;
    private Guid _activeClsid;
    private bool _disposed;

    public PreviewHandlerHost(PreviewHandlerPool pool, string path, Guid clsid)
    {
        _pool = pool;
        _targetPath = path;
        _targetClsid = clsid;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // Held for this host's whole lifetime, not just the initial activation: some registered handlers
        // (Office formats) are the full app EXE acting as its own prevhost surrogate, and interacting with
        // their rendered content -- not just their cold-start -- can pop up a real top-level window of
        // theirs (e.g. a right-click context menu), which would otherwise register as a foreground steal.
        // The quick window's foreground-loss hide (unrelated to this class) already tolerates that for as
        // long as the owning QuickLookWindow stays open (its own owned-window check); this makes the
        // separate, non-debounced foreground hook agree instead of hiding mid-interaction.
        PreviewActivationSignal.Begin();

        // A plain child window the handler renders into; WPF keeps it sized to this element's slot.
        _hostHwnd = PreviewHandlerInterop.CreateWindowEx(
            0, "static", null,
            PreviewHandlerInterop.WS_CHILD | PreviewHandlerInterop.WS_VISIBLE,
            0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        ScheduleRender();
        return new HandleRef(this, _hostHwnd);
    }

    // IReusablePreview: re-point this live host at a new file in place. Returns false for anything this
    // host can't render (a directory / no registered handler) so the caller falls back to rebuilding.
    public bool TrySetTarget(string path, bool isDir)
    {
        if (_disposed || isDir || string.IsNullOrEmpty(path)) return false;
        var clsid = PreviewHandlerRegistry.FindHandlerClsid(Path.GetExtension(path));
        if (clsid == null) return false;

        _targetPath = path;
        _targetClsid = clsid.Value;
        ScheduleRender();
        return true;
    }

    // Deferred + generation-guarded: Office/PDF cold start is slow, and rapid navigation must not queue a
    // render per keystroke — only the latest requested target actually renders.
    private void ScheduleRender()
    {
        var gen = ++_generation;
        Dispatcher.BeginInvoke(new Action(() => Render(gen)), DispatcherPriority.Background);
    }

    private void Render(int gen)
    {
        if (_disposed || gen != _generation || _hostHwnd == IntPtr.Zero) return;

        var clsid = _targetClsid;
        var path = _targetPath;
        try
        {
            if (_activeHandler == null || _activeClsid != clsid)
            {
                // Switch handlers: park the current one (Unload keeps it pooled), activate the new one.
                if (_activeHandler != null) { try { _activeHandler.Unload(); } catch { } }
                _activeComObject = _pool.Acquire(clsid);
                _activeHandler = _activeComObject as IPreviewHandler;
                _activeClsid = clsid;
                if (_activeHandler == null) return;
            }
            else
            {
                // Same handler, new file — unload the previous content before re-initializing.
                try { _activeHandler.Unload(); } catch { }
            }

            if (!InitializeHandler(_activeComObject!, path)) return;
            var rect = ClientRect();
            _activeHandler!.SetWindow(_hostHwnd, in rect);
            _activeHandler.DoPreview();
            GrantForegroundRights();
        }
        catch
        {
            // Handler failed on this file; leave the host blank rather than crash.
        }
    }

    // Grants the handler's out-of-process server the right to legitimately take OS foreground for its own
    // transient popups (a right-click menu, a dialog), since a freshly spawned process's inherited grant
    // (from being launched by us while we were foreground) doesn't extend to popups shown well after
    // startup. Doesn't help every case -- confirmed by testing that Office's own main window can still
    // immediately re-activate itself over its own just-shown popup afterward (both windows same process),
    // which no cross-process grant can prevent since a process never needs permission to activate its own
    // windows. Left in as a real, harmless improvement for handlers that don't have that self-competing
    // behavior, even though it's not a full fix for Office specifically.
    private void GrantForegroundRights()
    {
        try
        {
            var child = PreviewHandlerInterop.GetWindow(_hostHwnd, PreviewHandlerInterop.GW_CHILD);
            if (child == IntPtr.Zero) return;
            PreviewHandlerInterop.GetWindowThreadProcessId(child, out var pid);
            if (pid != 0) PreviewHandlerInterop.AllowSetForegroundWindow(pid);
        }
        catch { }
    }

    private static bool InitializeHandler(object com, string path)
    {
        if (com is IInitializeWithFile initFile)
        {
            initFile.Initialize(path, PreviewHandlerInterop.STGM_READ);
            return true;
        }
        if (com is IInitializeWithStream initStream)
        {
            if (PreviewHandlerInterop.SHCreateStreamOnFileEx(path, PreviewHandlerInterop.STGM_READ | PreviewHandlerInterop.STGM_SHARE_DENY_WRITE, 0, false, IntPtr.Zero, out var stream) == 0 && stream != null)
            {
                initStream.Initialize(stream, PreviewHandlerInterop.STGM_READ);
                return true;
            }
        }
        if (com is IInitializeWithItem initItem)
        {
            var iid = PreviewHandlerInterop.IID_IShellItem;
            if (PreviewHandlerInterop.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var item) == 0 && item != null)
            {
                initItem.Initialize(item, PreviewHandlerInterop.STGM_READ);
                return true;
            }
        }
        return false;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_activeHandler == null) return;
        var rect = ClientRect();
        try { _activeHandler.SetRect(in rect); } catch { }
    }

    private RECT ClientRect()
    {
        PreviewHandlerInterop.GetClientRect(_hostHwnd, out var rect);
        return rect;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _disposed = true;
        PreviewActivationSignal.End();
        // Park the active handler back in the pool (Unload only). The pool owns its lifetime and releases
        // it on EndPreviewSession — never FinalReleaseComObject here, or the cached prevhost would die.
        if (_activeHandler != null)
        {
            try { _activeHandler.Unload(); } catch { }
            _activeHandler = null;
            _activeComObject = null;
        }
        if (_hostHwnd != IntPtr.Zero)
        {
            PreviewHandlerInterop.DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }
    }
}
