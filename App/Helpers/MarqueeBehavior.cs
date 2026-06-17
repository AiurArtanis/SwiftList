using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwiftList.App.Helpers;

/// <summary>
/// Attached behavior that automatically scrolls TextBlock content when it overflows
/// and its parent ListBoxItem is selected or hovered.
/// </summary>
public static class MarqueeBehavior
{
    public static readonly DependencyProperty EnableMarqueeProperty =
        DependencyProperty.RegisterAttached("EnableMarquee", typeof(bool), typeof(MarqueeBehavior),
            new PropertyMetadata(false, OnEnableMarqueeChanged));

    public static bool GetEnableMarquee(DependencyObject obj) => (bool)obj.GetValue(EnableMarqueeProperty);
    public static void SetEnableMarquee(DependencyObject obj, bool value) => obj.SetValue(EnableMarqueeProperty, value);

    private static void OnEnableMarqueeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.Loaded -= Element_Loaded;
        element.Unloaded -= Element_Unloaded;

        if ((bool)e.NewValue)
        {
            element.Loaded += Element_Loaded;
            element.Unloaded += Element_Unloaded;
            if (element.IsLoaded)
            {
                InitializeMarquee(element);
            }
        }
        else
        {
            CleanupMarquee(element);
        }
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            InitializeMarquee(element);
        }
    }

    private static void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            CleanupMarquee(element);
        }
    }

    private static void InitializeMarquee(FrameworkElement element)
    {
        CleanupMarquee(element);

        var listBoxItem = FindVisualAncestor<ListBoxItem>(element);
        if (listBoxItem == null) return;

        if (element.RenderTransform is not TranslateTransform)
        {
            element.RenderTransform = new TranslateTransform();
        }

        var isMouseOverDescriptor = DependencyPropertyDescriptor.FromProperty(UIElement.IsMouseOverProperty, typeof(ListBoxItem));
        var isSelectedDescriptor = DependencyPropertyDescriptor.FromProperty(ListBoxItem.IsSelectedProperty, typeof(ListBoxItem));

        EventHandler handler = (s, e) => UpdateMarqueeAnimation(element, listBoxItem);

        isMouseOverDescriptor?.AddValueChanged(listBoxItem, handler);
        isSelectedDescriptor?.AddValueChanged(listBoxItem, handler);

        element.SizeChanged += (s, e) => UpdateMarqueeAnimation(element, listBoxItem);

        if (VisualTreeHelper.GetParent(element) is FrameworkElement parent)
        {
            parent.SizeChanged += (s, e) => UpdateMarqueeAnimation(element, listBoxItem);
        }

        var state = new MarqueeState
        {
            ListBoxItem = listBoxItem,
            IsMouseOverDescriptor = isMouseOverDescriptor,
            IsSelectedDescriptor = isSelectedDescriptor,
            Handler = handler
        };
        SetMarqueeState(element, state);

        UpdateMarqueeAnimation(element, listBoxItem);
    }

    private static void CleanupMarquee(FrameworkElement element)
    {
        var state = GetMarqueeState(element);
        if (state != null)
        {
            if (state.ListBoxItem != null && state.Handler != null)
            {
                state.IsMouseOverDescriptor?.RemoveValueChanged(state.ListBoxItem, state.Handler);
                state.IsSelectedDescriptor?.RemoveValueChanged(state.ListBoxItem, state.Handler);
            }
            SetMarqueeState(element, null);
        }

        if (element.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }
    }

    private static void UpdateMarqueeAnimation(FrameworkElement element, ListBoxItem listBoxItem)
    {
        if (element.RenderTransform is not TranslateTransform translate) return;

        if (VisualTreeHelper.GetParent(element) is not FrameworkElement parent) return;

        var availableWidth = parent.ActualWidth;
        var elementWidth = element.ActualWidth;

        if (availableWidth <= 0 || elementWidth <= 0) return;

        var overflow = elementWidth - availableWidth;
        var shouldAnimate = overflow > 0 && (listBoxItem.IsMouseOver || listBoxItem.IsSelected);

        if (shouldAnimate)
        {
            var speed = 40.0; // pixels per second
            var durationSeconds = overflow / speed;

            var keyFrameAnimation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };

            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8 + durationSeconds))));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8 + durationSeconds + 1.0))));

            translate.BeginAnimation(TranslateTransform.XProperty, keyFrameAnimation);
        }
        else
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T ancestor) return ancestor;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static readonly DependencyProperty MarqueeStateProperty =
        DependencyProperty.RegisterAttached("MarqueeState", typeof(MarqueeState), typeof(MarqueeBehavior), new PropertyMetadata(null));

    private static MarqueeState? GetMarqueeState(DependencyObject obj) => (MarqueeState?)obj.GetValue(MarqueeStateProperty);
    private static void SetMarqueeState(DependencyObject obj, MarqueeState? value) => obj.SetValue(MarqueeStateProperty, value);

    private class MarqueeState
    {
        public ListBoxItem? ListBoxItem { get; set; }
        public DependencyPropertyDescriptor? IsMouseOverDescriptor { get; set; }
        public DependencyPropertyDescriptor? IsSelectedDescriptor { get; set; }
        public EventHandler? Handler { get; set; }
    }
}
