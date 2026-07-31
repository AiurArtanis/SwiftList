using System.Windows;

namespace SwiftList.Plugins.WindowGuides;

internal static class GuideSnapCalculator
{
    internal const double SnapThresholdPixels = 10;

    internal static Rect Snap(Rect windowBounds, Point screenCenter)
    {
        var center = new Point(windowBounds.Left + windowBounds.Width / 2, windowBounds.Top + windowBounds.Height / 2);
        var left = Math.Abs(center.X - screenCenter.X) <= SnapThresholdPixels
            ? screenCenter.X - windowBounds.Width / 2
            : windowBounds.Left;
        var top = Math.Abs(center.Y - screenCenter.Y) <= SnapThresholdPixels
            ? screenCenter.Y - windowBounds.Height / 2
            : windowBounds.Top;
        return new Rect(left, top, windowBounds.Width, windowBounds.Height);
    }
}
