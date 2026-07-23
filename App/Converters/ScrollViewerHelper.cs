using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftList.App.Converters;

public static class ScrollViewerHelper
{
    public static readonly DependencyProperty ShiftWheelScrollsHorizontallyProperty =
        DependencyProperty.RegisterAttached("ShiftWheelScrollsHorizontally", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnShiftWheelScrollsHorizontallyChanged));

    public static bool GetShiftWheelScrollsHorizontally(DependencyObject obj) => (bool)obj.GetValue(ShiftWheelScrollsHorizontallyProperty);
    public static void SetShiftWheelScrollsHorizontally(DependencyObject obj, bool value) => obj.SetValue(ShiftWheelScrollsHorizontallyProperty, value);

    private static void OnShiftWheelScrollsHorizontallyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (e.Delta > 0)
            {
                scrollViewer.LineLeft();
            }
            else
            {
                scrollViewer.LineRight();
            }
            e.Handled = true;
        }
    }

    public static readonly DependencyProperty BubbleMouseWheelProperty =
        DependencyProperty.RegisterAttached("BubbleMouseWheel", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnBubbleMouseWheelChanged));

    public static bool GetBubbleMouseWheel(DependencyObject obj) => (bool)obj.GetValue(BubbleMouseWheelProperty);
    public static void SetBubbleMouseWheel(DependencyObject obj, bool value) => obj.SetValue(BubbleMouseWheelProperty, value);

    private static void OnBubbleMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PreviewMouseWheel -= OnElementPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnElementPreviewMouseWheel;
        }
    }

    private static void OnElementPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement element) return;

        if (VisualTreeHelper.GetParent(element) is UIElement parent)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parent.RaiseEvent(eventArg);
        }
    }
}
