using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using SwiftList.PluginSdk.Services;

using SwiftList.App.Services.AppWindow;
using SwiftList.App.Services.Plugin;
using SwiftList.PluginSdk.Abstractions.Plugins.Preview;
namespace SwiftList.App.Services;

public class QuickLookManager
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static readonly Lazy<QuickLookManager> _instance = new(() => new QuickLookManager());
    public static QuickLookManager Instance => _instance.Value;

    private Views.QuickLook.QuickLookWindow? _window;
    private Window? _owner;
    private bool _userWantsPreview;
    // Tracked separately (not just "is _owner non-null") since external-preview mode attaches
    // LocationChanged/SizeChanged but deliberately NOT Deactivated -- see ShowOrUpdate's own comment.
    private bool _ownerTrackingAttached;
    private bool _ownerDeactivateAttached;
    // Set while both windows are hidden for a preview handler's own popup dialog (see
    // PreviewDialogSignal) -- distinguishes that from every other reason _window/_owner might be
    // hidden, so DialogClosed only ever re-shows what this specific mechanism hid.
    private bool _hiddenForDialog;

    // Owners whose Closed we've hooked (once each) to end the preview session — release pooled native
    // preview handlers and their prevhost surrogates when the search window that used them goes away.
    private readonly HashSet<Window> _sessionOwners = new();

    private QuickLookManager()
    {
        PreviewActivationSignal.FocusStolen += OnPreviewFocusStolen;
        PreviewDialogSignal.DialogOpened += OnPreviewDialogOpened;
        PreviewDialogSignal.DialogClosed += OnPreviewDialogClosed;
    }

    // A preview handler's own popup (e.g. Word's "Enter password" prompt) just got the OS foreground --
    // see PreviewFocusGuard's own comment. Left floating on top of it, the quick window and its preview
    // window make that dialog unreachable, so both hide for as long as it's up. Runs on whatever thread
    // the plugin's WinEvent hook fires on, not necessarily this app's UI thread, so both handlers marshal
    // onto the owner's Dispatcher before touching either Window.
    private void OnPreviewDialogOpened()
    {
        if (_owner == null || _window == null) return;
        _owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_owner == null || _window == null) return;
            _hiddenForDialog = true;
            _window.Hide();
            _owner.Hide();
        }));
    }

    private void OnPreviewDialogClosed()
    {
        if (!_hiddenForDialog) return;
        var owner = _owner;
        var window = _window;
        if (owner == null || window == null) { _hiddenForDialog = false; return; }
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            _hiddenForDialog = false;
            owner.Show();
            window.Show();
        }));
    }

    // A native (HwndHost) preview handler's own out-of-process window just grabbed OS keyboard focus for
    // itself (see PreviewFocusGuard) -- reclaim it back onto the search box the preview is attached to.
    // Window.Deactivated doesn't fire for this: the handler's window is reparented as a child of our own
    // overlay window, so top-level activation never actually changes, only which control has keyboard
    // focus.
    private void OnPreviewFocusStolen()
    {
        if (_owner is ISearchWindow searchWindow && IsVisible)
            _owner.Dispatcher.BeginInvoke(new Action(() => searchWindow.FocusSearch()));
    }

    // IsShowingExternalPreview counts too: that path deliberately Hide()s _window itself (so it never
    // shows an empty panel next to whatever the provider popped up externally) -- without this, Toggle()
    // would read "nothing is showing" while QuickLook is actively docked and take the wrong branch (show
    // again instead of hide) on the next Alt+P.
    public bool IsVisible => _window != null && (_window.IsVisible || _window.IsShowingExternalPreview);

    // Checked by QuickSearchWindow.Window_Deactivated so its own delayed auto-hide-on-deactivate logic
    // doesn't fight this: without it, that handler would see the window we just Hide()'d as "deactivated"
    // and run the FULL HideWindow() (resets the search query, stops the foreground hook, ...) a moment
    // later, undoing the purely-visual, preserve-everything hide this is meant to be.
    public bool IsHiddenForDialog => _hiddenForDialog;

    public void Reset()
    {
        _userWantsPreview = false;
        Hide();
    }

    public void Toggle(Window owner, string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (IsVisible)
        {
            _userWantsPreview = false;
            Hide();
        }
        else
        {
            _userWantsPreview = true;
            ShowOrUpdate(owner, path);
        }
    }

    public void UpdateOrShow(Window owner, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Hide();
            return;
        }

        if (_userWantsPreview)
        {
            ShowOrUpdate(owner, path);
        }
    }

    public void Hide()
    {
        if (_window != null)
        {
            _window.Hide();
            // Not redundant with the line above: when the current provider is RendersExternally, _window
            // was already hidden the moment that provider started showing, so this Hide() call is a no-op
            // transition-wise and IsVisibleChanged (which normally does this) never fires again.
            _window.ReleaseCurrentPreview();
            DetachOwner();
        }
    }

    private void ShowOrUpdate(Window owner, string path)
    {
        _owner = owner;

        // Keep the preview-handler pool alive across this owner's hide/show cycles; release it only when
        // the owner window itself closes. Hooked once per owner (self-removes on close).
        if (_sessionOwners.Add(owner))
            owner.Closed += OnSessionOwnerClosed;

        if (_window == null)
        {
            _window = new Views.QuickLook.QuickLookWindow();
            _window.Closed += (s, e) => _window = null;
        }

        _window.SetTarget(path);

        // The winning provider's real preview surface is a separate window it manages itself (e.g. an
        // external application) -- CreatePreview's returned content is never actually shown, so our own
        // panel would just be a redundant empty box floating next to whatever that provider popped up.
        // Checked here, right after SetTarget resolves the winning provider, specifically to avoid the
        // isFirstShow/_window.Show() logic below re-showing it: SetTarget runs first and can itself leave
        // _window.IsVisible false, which isFirstShow would otherwise read as "starting fresh" and undo.
        if (_window.IsShowingExternalPreview)
        {
            if (_window.IsVisible) _window.Hide();

            // Still track the owner moving/resizing (so the docked window follows it around), but NOT
            // Deactivated: that handler's Hide() would close the very window the user just clicked into --
            // clicking QuickLook's own docked window (a separate top-level window) deactivates the owner
            // for real, same reasoning as Owner_Deactivated's existing HwndHost comment, but there's no
            // equivalent "still focus in a way we care about" check possible for a foreign process.
            DetachOwnerDeactivateTracking();
            AttachOwnerLocationTracking(owner);

            NotifyExternalBounds(owner);
            return;
        }

        AttachOwnerLocationTracking(owner);
        AttachOwnerDeactivateTracking(owner);

        // Only slide in on the transition to visible -- a preview session starting fresh -- not on every
        // reposition while it's already open (the owner moving/resizing would otherwise re-trigger the
        // slide constantly instead of just tracking along).
        var isFirstShow = !_window.IsVisible;
        if (isFirstShow)
        {
            _window.Owner = owner;
            _window.Show();
        }

        PositionWindow(animate: isFirstShow);
    }

    private void AttachOwnerLocationTracking(Window owner)
    {
        if (_ownerTrackingAttached) return;
        owner.LocationChanged += Owner_LocationChanged;
        owner.SizeChanged += Owner_SizeChanged;
        _ownerTrackingAttached = true;
    }

    private void AttachOwnerDeactivateTracking(Window owner)
    {
        if (_ownerDeactivateAttached) return;
        owner.Deactivated += Owner_Deactivated;
        _ownerDeactivateAttached = true;
    }

    private void DetachOwnerDeactivateTracking()
    {
        if (!_ownerDeactivateAttached || _owner == null) return;
        _owner.Deactivated -= Owner_Deactivated;
        _ownerDeactivateAttached = false;
    }

    private void DetachOwner()
    {
        if (_owner != null)
        {
            if (_ownerTrackingAttached)
            {
                _owner.LocationChanged -= Owner_LocationChanged;
                _owner.SizeChanged -= Owner_SizeChanged;
                _ownerTrackingAttached = false;
            }
            DetachOwnerDeactivateTracking();
            _owner = null;
        }
    }

    // Branches on the current mode: external-dock re-asserts QuickLook's window position, the normal
    // path repositions our own _window -- both are hooked to the same owner LocationChanged/SizeChanged
    // events (see AttachOwnerLocationTracking), just handled differently depending on which is active.
    private void RepositionForCurrentMode()
    {
        if (_window == null || _owner == null) return;
        if (_window.IsShowingExternalPreview) NotifyExternalBounds(_owner);
        else PositionWindow();
    }

    private void Owner_LocationChanged(object? sender, EventArgs e) => RepositionForCurrentMode();
    private void Owner_SizeChanged(object? sender, SizeChangedEventArgs e) => RepositionForCurrentMode();

    private void Owner_Deactivated(object? sender, EventArgs e)
    {
        // A real (HwndHost) preview -- e.g. a native document/media preview handler -- needs actual focus
        // to be interactive (scrolling, playback controls), so clicking into it deactivates the owner for
        // real. Without this check, that click would immediately hide the very preview the user just
        // clicked into. Only hide when something outside this process took the foreground.
        if (IsForegroundWindowInThisProcess())
            return;
        Hide();
    }

    private static bool IsForegroundWindowInThisProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void OnSessionOwnerClosed(object? sender, EventArgs e)
    {
        if (sender is Window w)
        {
            w.Closed -= OnSessionOwnerClosed;
            _sessionOwners.Remove(w);
        }
        // The owner is already deactivated → QuickLook hidden → any visible host parked its handler back
        // in the pool, so releasing now can't blank a live preview.
        foreach (var provider in PluginManager.Instance.FilePreviewProviders)
            (provider as IPreviewSessionAware)?.EndPreviewSession();
    }

    private void PositionWindow(bool animate = false)
    {
        if (_window == null || _owner == null || !_window.IsVisible) return;

        var computed = TryComputeTargetRect(_owner);
        if (computed == null) return;
        var rect = computed.Value;

        try
        {
            _window.Width = rect.OuterWidth;
            _window.Height = rect.OuterHeight;

            // Clear any still-running/held slide-in animation before touching Left directly -- WPF keeps
            // an animated dependency property pinned to the animation's value until the clock is cleared,
            // so a bare assignment here would silently be ignored while one is active.
            _window.BeginAnimation(Window.LeftProperty, null);
            _window.Top = rect.OuterTop;

            if (animate)
            {
                // Slide out like a drawer: start just short of the resting spot, on the side it docked
                // to, and ease out to it -- rather than just snapping into place.
                const double SlideDistance = 40;
                var startLeft = rect.DockedRight ? rect.OuterLeft - SlideDistance : rect.OuterLeft + SlideDistance;
                _window.Left = startLeft;

                var slideIn = new DoubleAnimation(startLeft, rect.OuterLeft, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                _window.BeginAnimation(Window.LeftProperty, slideIn);
            }
            else
            {
                _window.Left = rect.OuterLeft;
            }
        }
        catch { }
    }

    // A real top-level window (no invisible shadow-margin border like our own _window has) sits a bit
    // further out/wider than where our own panel's outer bounds would land -- these are on top of
    // whatever gap TryComputeTargetRect already used, tuned by eye against an actual docked QuickLook
    // window rather than derived from anything measurable.
    private const double ExternalDockExtraGap = 0;
    private const double ExternalDockExtraWidth = 80;

    // Tells the winning provider (if it wants to know -- see IReceivesPreviewPanelBounds) where our own
    // panel would have gone for this owner, in physical screen pixels: a provider positioning an
    // externally-managed window needs raw pixel SetWindowPos coordinates, not WPF's DIP space, and it has
    // no way to do this DPI/monitor-work-area math itself (that's owner-window state this class already
    // tracks). Best-effort -- silently does nothing if the geometry can't be computed or the window
    // doesn't implement the interface.
    private void NotifyExternalBounds(Window owner)
    {
        if (_window == null) return;
        var computed = TryComputeTargetRect(owner, ExternalDockExtraGap, ExternalDockExtraWidth);
        if (computed == null) return;
        var rect = computed.Value;

        // DIP = physical * dpiScale (see TryComputeTargetRect), so invert it back to physical pixels. No
        // outer/visible distinction here -- an externally-managed window has no invisible margin to
        // compensate for, so it's positioned at the true visible rectangle directly.
        var left = (int)Math.Round(rect.VisibleLeft / rect.DpiScale);
        var top = (int)Math.Round(rect.VisibleTop / rect.DpiScale);
        var width = (int)Math.Round(rect.VisibleWidth / rect.DpiScale);
        var height = (int)Math.Round(rect.VisibleHeight / rect.DpiScale);

        try { _window.NotifyExternalPreviewBounds(left, top, width, height); }
        catch { }
    }

    // "Visible" = the actual rounded-corner card a user sees. Our own _window pads an extra
    // ContentMargin on every side around that for its invisible drop-shadow border, so its outer WPF
    // Window bounds (Outer*) differ from the visible rectangle -- Outer* exists so PositionWindow can
    // recover exactly the same outer bounds the pre-refactor code computed directly, while
    // NotifyExternalBounds uses Visible* as-is, since an external window has no such margin to add back.
    private readonly struct TargetRect
    {
        public double VisibleLeft { get; init; }
        public double VisibleTop { get; init; }
        public double VisibleWidth { get; init; }
        public double VisibleHeight { get; init; }
        public double OuterMargin { get; init; }
        public double DpiScale { get; init; }
        public bool DockedRight { get; init; }

        public double OuterLeft => VisibleLeft - OuterMargin;
        public double OuterTop => VisibleTop - OuterMargin;
        public double OuterWidth => VisibleWidth + 2 * OuterMargin;
        public double OuterHeight => VisibleHeight + 2 * OuterMargin;
    }

    // Shared by PositionWindow (moves our own _window there) and NotifyExternalBounds (hands a rectangle
    // to an external-preview provider instead) -- both need the same "where would the preview panel go
    // for this owner" answer, computed once instead of duplicated and risking drift. extraGap/extraWidth
    // default to 0 so the normal (own-window) path is untouched; NotifyExternalBounds passes the
    // ExternalDock* tuning constants above.
    private static TargetRect? TryComputeTargetRect(Window owner, double extraGap = 0, double extraWidth = 0)
    {
        try
        {
            var ownerLeft = owner.Left;
            var ownerTop = owner.Top;
            var ownerWidth = owner.ActualWidth;

            // Use the work area of the monitor the owner is actually on -- not the primary monitor --
            // so the right/left placement flip is correct when the search window sits on a secondary screen.
            var ownerHandle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            var workingArea = Screen.FromHandle(ownerHandle).WorkingArea;
            var dpiScale = 1.0;
            var src = PresentationSource.FromVisual(owner);
            if (src?.CompositionTarget != null) dpiScale = src.CompositionTarget.TransformFromDevice.M11;
            // physical (system-DPI space) -> DIP
            var screenLeft = workingArea.Left * dpiScale;
            var screenTop = workingArea.Top * dpiScale;
            var screenRight = workingArea.Right * dpiScale;
            var screenBottom = workingArea.Bottom * dpiScale;

            var previewInset = Views.QuickLook.QuickLookWindow.ContentMargin;

            // Fixed, user-configurable size (General settings page) rather than mirroring the owner's
            // current ActualHeight -- the owner auto-sizes to however many results are actually showing,
            // so a preview window that copied it would resize unpredictably every time the result count
            // changed instead of staying the same size like a real preview pane. Capped to the current
            // monitor's own work area -- repositioning alone can't keep a configured size fully on screen
            // when that size is bigger than the monitor itself (e.g. the 1200px max preview height on a
            // 768px-tall laptop display).
            var visibleWidth = Math.Min(UiMetrics.PreviewWindowWidth - 2 * previewInset, screenRight - screenLeft) + extraWidth;
            var visibleHeight = Math.Min(UiMetrics.PreviewWindowHeight - 2 * previewInset, screenBottom - screenTop);

            const double DesiredGap = 10;
            var ownerInset = (owner as IHasVisibleContentInset)?.VisibleContentInset ?? new Thickness(0);
            var gap = DesiredGap + extraGap;

            var dockedRight = true;
            var visibleLeft = ownerLeft + ownerWidth - ownerInset.Right + gap;
            if (visibleLeft + visibleWidth > screenRight)
            {
                visibleLeft = ownerLeft + ownerInset.Left - gap - visibleWidth;
                dockedRight = false;
            }
            var visibleTop = ownerTop + ownerInset.Top;

            // Neither docking side, nor the owner's own vertical position, guarantees the preview's
            // configured size (user-configurable, up to 900x1200) actually fits next to the owner on
            // this monitor -- clamp against the monitor's work area on every edge so a large preview
            // window always stays fully visible instead of running off-screen.
            var minLeft = screenLeft;
            var maxLeft = screenRight - visibleWidth;
            visibleLeft = Math.Clamp(visibleLeft, minLeft, Math.Max(minLeft, maxLeft));

            var minTop = screenTop;
            var maxTop = screenBottom - visibleHeight;
            visibleTop = Math.Clamp(visibleTop, minTop, Math.Max(minTop, maxTop));

            return new TargetRect
            {
                VisibleLeft = visibleLeft,
                VisibleTop = visibleTop,
                VisibleWidth = visibleWidth,
                VisibleHeight = visibleHeight,
                OuterMargin = previewInset,
                DpiScale = dpiScale,
                DockedRight = dockedRight
            };
        }
        catch
        {
            return null;
        }
    }
}
