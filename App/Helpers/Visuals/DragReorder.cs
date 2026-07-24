using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace SwiftList.App.Helpers.Visuals;

/// <summary>
/// Drag-to-reorder for an ItemsControl bound to an ObservableCollection (or any IList) -- WPF has no
/// built-in support for this. Operates purely through the non-generic IList interface every
/// ObservableCollection&lt;T&gt; implements, so one attached behavior covers every reorderable settings
/// list in the app (Favorites, Result Type Priority, Quick Navigation, Startup Panel tabs, sidebar filter
/// groups, results columns, ...) regardless of each one's own item type. Coexists with an existing
/// MoveUp/MoveDown button pair in the same item template -- this doesn't replace them (keyboard/
/// accessibility users still need a non-drag way to reorder), it just adds a mouse-drag shortcut, only
/// startable from a dedicated grip icon (see IsHandle) rather than anywhere on the row.
/// </summary>
public static class DragReorder
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(ItemsControl control, bool value) => control.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(ItemsControl control) => (bool)control.GetValue(IsEnabledProperty);

    // Marks the one small element within each item template (a grip icon, typically at the row's left
    // edge) that's allowed to start a drag -- without this, a mouse-down anywhere else on the row (its
    // label text, its background) would also pick it up, which reads as accidental/surprising rather
    // than deliberate. Only a Button/TextBox press is excluded automatically (see IsWithinHandle);
    // everything else needs to opt in explicitly via this property.
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static void SetIsHandle(FrameworkElement element, bool value) => element.SetValue(IsHandleProperty, value);
    public static bool GetIsHandle(FrameworkElement element) => (bool)element.GetValue(IsHandleProperty);

    // Keyed per-ItemsControl (not a single shared field) so two reorderable lists open in the same
    // window at once (e.g. this settings page's own sidebar-order and column-order cards) never
    // interfere with each other's in-progress drag.
    private static readonly Dictionary<ItemsControl, (Point start, bool onHandle, object? item)> _state = new();
    private static readonly Dictionary<ItemsControl, (AdornerLayer layer, DragAdorner adorner, FrameworkElement container)> _drag = new();

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl control || e.NewValue is not true) return;

        control.AllowDrop = true;
        control.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        control.PreviewMouseMove += OnPreviewMouseMove;
        control.DragOver += OnDragOver;
        control.Drop += OnDrop;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var control = (ItemsControl)sender;
        var onHandle = IsWithinHandle(e.OriginalSource as DependencyObject, control);
        _state[control] = (e.GetPosition(control), onHandle, null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var control = (ItemsControl)sender;
        if (!_state.TryGetValue(control, out var s) || !s.onHandle) return;

        var pos = e.GetPosition(control);
        if (Math.Abs(pos.X - s.start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - s.start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var container = FindContainer(e.OriginalSource as DependencyObject, control);
        if (container == null) return;

        var item = control.ItemContainerGenerator.ItemFromContainer(container);
        if (item == null) return;

        _state[control] = (s.start, s.onHandle, item);

        // Renders a floating, drop-shadowed snapshot of the whole row that follows the cursor (updated
        // in OnDragOver below) so the drag actually reads as "picking the row up," not just a bare
        // cursor change -- the original row dims in place to mark where it's being lifted from.
        var layer = AdornerLayer.GetAdornerLayer(control);
        if (layer != null)
        {
            var adorner = new DragAdorner(container, control, e.GetPosition(control));
            layer.Add(adorner);
            _drag[control] = (layer, adorner, container);
        }
        container.Opacity = 0.35;

        try
        {
            DragDrop.DoDragDrop(container, item, DragDropEffects.Move);
        }
        finally
        {
            container.Opacity = 1.0;
            if (_drag.TryGetValue(control, out var d))
            {
                d.layer.Remove(d.adorner);
                _drag.Remove(control);
            }

            // Removed rather than reset to a neutral value: leaving a stale entry behind (even with
            // onHandle/item cleared) is exactly what let a stray MouseMove right after this DoDragDrop
            // call resume as if still mid-drag, occasionally making the whole row draggable again until
            // the next real mouse-down. Only a fresh PreviewMouseLeftButtonDown may repopulate this.
            _state.Remove(control);
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var control = (ItemsControl)sender;
        if (_drag.TryGetValue(control, out var d))
            d.adorner.UpdatePosition(e.GetPosition(control));
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        // _state[control] is cleared in OnPreviewMouseMove's own finally block once DoDragDrop
        // returns (which happens right after this handler runs), so this only reads it, never resets it.
        var control = (ItemsControl)sender;
        if (!_state.TryGetValue(control, out var s) || s.item == null) return;

        if (control.ItemsSource is not IList list) return;

        var oldIndex = list.IndexOf(s.item);
        if (oldIndex < 0) return;

        var targetContainer = FindContainer(e.OriginalSource as DependencyObject, control);
        var targetItem = targetContainer != null ? control.ItemContainerGenerator.ItemFromContainer(targetContainer) : null;
        var newIndex = targetItem != null ? list.IndexOf(targetItem) : list.Count - 1;

        if (newIndex < 0 || newIndex == oldIndex) return;

        list.RemoveAt(oldIndex);
        list.Insert(newIndex, s.item);
    }

    // A Button/TextBox press (Move Up/Down, Edit, Remove, ...) always wins even if it happens to sit
    // inside a marked handle -- IsHandle is meant for otherwise-inert grip icons, not interactive
    // controls, but this keeps that true regardless of how a template composes the two.
    private static bool IsWithinHandle(DependencyObject? source, ItemsControl control)
    {
        while (source != null && source != control)
        {
            if (source is ButtonBase or TextBoxBase)
                return false;
            if (source is FrameworkElement fe && GetIsHandle(fe))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    // Walks up from whatever was actually clicked/dropped on to the realized item container
    // ItemContainerGenerator knows about -- VirtualizingStackPanel means only currently-visible
    // containers exist at all, which is exactly what a live mouse event can ever land on anyway.
    private static FrameworkElement? FindContainer(DependencyObject? source, ItemsControl control)
    {
        while (source != null && source != control)
        {
            if (source is FrameworkElement fe && control.ItemContainerGenerator.IndexFromContainer(fe) >= 0)
                return fe;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    // A VisualBrush snapshot of the dragged row, hosted in a real Border child (not just painted in
    // OnRender) specifically so it can carry a genuine DropShadowEffect -- Adorner.OnRender's
    // DrawingContext has no Effect concept of its own.
    private sealed class DragAdorner : Adorner
    {
        private readonly Border _visual;
        private Point _position;

        public DragAdorner(FrameworkElement source, UIElement adornedElement, Point startPosition) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _position = startPosition;

            _visual = new Border
            {
                Width = source.ActualWidth,
                Height = source.ActualHeight,
                Background = new VisualBrush(source) { Stretch = Stretch.None },
                Opacity = 0.85,
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
            };
            AddVisualChild(_visual);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;

        protected override Size MeasureOverride(Size constraint)
        {
            _visual.Measure(constraint);
            return _visual.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _visual.Arrange(new Rect(_position.X - _visual.Width / 2, _position.Y - _visual.Height / 2, _visual.Width, _visual.Height));
            return finalSize;
        }

        public void UpdatePosition(Point position)
        {
            _position = position;
            InvalidateArrange();
        }
    }
}
