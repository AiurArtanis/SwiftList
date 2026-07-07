using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using SwiftList.PluginSdk.Abstractions.Plugins;

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

    // Owners whose Closed we've hooked (once each) to end the preview session — release pooled native
    // preview handlers and their prevhost surrogates when the search window that used them goes away.
    private readonly HashSet<Window> _sessionOwners = new();

    private QuickLookManager() { }

    public bool IsVisible => _window != null && _window.IsVisible;

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

        // Only slide in on the transition to visible -- a preview session starting fresh -- not on every
        // reposition while it's already open (the owner moving/resizing would otherwise re-trigger the
        // slide constantly instead of just tracking along).
        var isFirstShow = !_window.IsVisible;
        if (isFirstShow)
        {
            _window.Owner = owner;
            _window.Show();

            // Attach window position tracking
            owner.LocationChanged += Owner_LocationChanged;
            owner.SizeChanged += Owner_SizeChanged;
            owner.Deactivated += Owner_Deactivated;
        }

        PositionWindow(animate: isFirstShow);
    }

    private void DetachOwner()
    {
        if (_owner != null)
        {
            _owner.LocationChanged -= Owner_LocationChanged;
            _owner.SizeChanged -= Owner_SizeChanged;
            _owner.Deactivated -= Owner_Deactivated;
            _owner = null;
        }
    }

    private void Owner_LocationChanged(object? sender, EventArgs e) => PositionWindow();
    private void Owner_SizeChanged(object? sender, SizeChangedEventArgs e) => PositionWindow();

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

        try
        {
            var ownerLeft = _owner.Left;
            var ownerTop = _owner.Top;
            var ownerWidth = _owner.ActualWidth;

            // Fixed, user-configurable size (General settings page) rather than mirroring the owner's
            // current ActualHeight -- the owner auto-sizes to however many results are actually showing,
            // so a preview window that copied it would resize unpredictably every time the result count
            // changed instead of staying the same size like a real preview pane.
            _window.Width = UiMetrics.PreviewWindowWidth;
            _window.Height = UiMetrics.PreviewWindowHeight;

            // Use the work area of the monitor the owner is actually on -- not the primary monitor --
            // so the right/left placement flip is correct when the search window sits on a secondary screen.
            var ownerHandle = new System.Windows.Interop.WindowInteropHelper(_owner).Handle;
            var workingArea = Screen.FromHandle(ownerHandle).WorkingArea;
            var dpiScale = 1.0;
            var src = PresentationSource.FromVisual(_owner);
            if (src?.CompositionTarget != null) dpiScale = src.CompositionTarget.TransformFromDevice.M11;
            var screenRight = workingArea.Right * dpiScale; // physical (system-DPI space) -> DIP

            // Both the owner and this preview window use AllowsTransparency with an invisible margin
            // around their actual visible card (room for a drop shadow) -- dock against those visible
            // edges, not the outer window bounds, or the gap ends up several times bigger than DesiredGap.
            const double DesiredGap = 10;
            var ownerInset = (_owner as IHasVisibleContentInset)?.VisibleContentInset ?? new Thickness(0);
            var previewInset = Views.QuickLook.QuickLookWindow.ContentMargin;

            var dockedRight = true;
            var targetLeft = ownerLeft + ownerWidth - ownerInset.Right + DesiredGap - previewInset;
            if (targetLeft + _window.Width > screenRight)
            {
                targetLeft = ownerLeft + ownerInset.Left - DesiredGap - _window.Width + previewInset;
                dockedRight = false;
            }
            var targetTop = ownerTop + ownerInset.Top - previewInset;

            // Clear any still-running/held slide-in animation before touching Left directly -- WPF keeps
            // an animated dependency property pinned to the animation's value until the clock is cleared,
            // so a bare assignment here would silently be ignored while one is active.
            _window.BeginAnimation(Window.LeftProperty, null);
            _window.Top = targetTop;

            if (animate)
            {
                // Slide out like a drawer: start just short of the resting spot, on the side it docked
                // to, and ease out to it -- rather than just snapping into place.
                const double SlideDistance = 40;
                var startLeft = dockedRight ? targetLeft - SlideDistance : targetLeft + SlideDistance;
                _window.Left = startLeft;

                var slideIn = new DoubleAnimation(startLeft, targetLeft, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                _window.BeginAnimation(Window.LeftProperty, slideIn);
            }
            else
            {
                _window.Left = targetLeft;
            }
        }
        catch { }
    }
}
