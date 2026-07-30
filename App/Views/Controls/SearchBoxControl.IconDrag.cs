using System.Windows;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SwiftList.App;

public partial class SearchBoxControl
{
    public event Action? IconDragCompleted;
    public event Action? IconDragStarted;
    public event Action? IconDragMoved;

    private Point? _iconPressScreenPoint;
    private bool _iconDragStarted;
    private WindowDragTracker? _iconDragTracker;

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsIconClickable) return;
        e.Handled = true;
        if (IsIconDraggable && sender is IInputElement inputElement)
        {
            _iconDragStarted = false;
            _iconPressScreenPoint = inputElement is System.Windows.Media.Visual visual ? visual.PointToScreen(e.GetPosition(inputElement)) : null;
            Mouse.Capture(inputElement);
        }
    }

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsIconDraggable || _iconPressScreenPoint == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not System.Windows.Media.Visual visual) return;
        var current = visual.PointToScreen(e.GetPosition((IInputElement)sender));
        if (!_iconDragStarted)
        {
            var delta = current - _iconPressScreenPoint.Value;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var window = Window.GetWindow(this);
            if (window == null) return;
            _iconDragStarted = true;
            _iconDragTracker = new WindowDragTracker(window);
            _iconDragTracker.Start(current);
            IconDragStarted?.Invoke();
            return;
        }
        _iconDragTracker?.Update(current);
        IconDragMoved?.Invoke();
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is IInputElement inputElement) inputElement.ReleaseMouseCapture();
        var wasDrag = _iconDragStarted;
        _iconDragStarted = false;
        _iconPressScreenPoint = null;
        if (wasDrag)
        {
            _iconDragTracker?.End();
            _iconDragTracker = null;
            IconDragCompleted?.Invoke();
            return;
        }
        if (!IsIconClickable || IconLeftClicked == null) return;
        var screenPoint = ((System.Windows.Media.Visual)sender).PointToScreen(e.GetPosition((IInputElement)sender));
        IconLeftClicked.Invoke((int)screenPoint.X, (int)screenPoint.Y);
    }
}
