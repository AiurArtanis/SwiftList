using System.Windows;
using System.Windows.Input;
using SwiftList.App.Helpers.Visuals;
using SwiftList.App.Services.Plugin;
using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowEffects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SwiftList.App;

public partial class QuickSearchWindow
{
    private readonly WindowDragTracker _borderDragTracker;

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is IInputElement inputElement) inputElement.CaptureMouse();
        _borderDragTracker.Start(PointToScreen(e.GetPosition(this)));
        NotifyDragStarted();
    }

    private void Border_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_borderDragTracker.IsDragging || e.LeftButton != MouseButtonState.Pressed) return;
        _borderDragTracker.Update(PointToScreen(e.GetPosition(this)));
        NotifyDragMoved();
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_borderDragTracker.IsDragging) return;
        _borderDragTracker.End();
        NotifyDragEnded();
        if (sender is IInputElement inputElement) inputElement.ReleaseMouseCapture();
        _controller.SaveWindowPosition();
    }

    private void NotifyDragStarted() => NotifyDragEffects(effect => effect.OnDragStarted(this));
    private void NotifyDragMoved() => NotifyDragEffects(effect => effect.OnDragMoved(this, SearchCardBorder));
    private void NotifyDragEnded() => NotifyDragEffects(effect => effect.OnDragEnded(this));
    private void SaveWindowPosition() => _controller.SaveWindowPosition();

    private static void NotifyDragEffects(Action<IQuickSearchWindowDragEffectProvider> notification)
    {
        foreach (var effect in PluginManager.Instance.QuickSearchWindowDragEffectProviders)
        {
            try { notification(effect); }
            catch (Exception ex) { Logger.Log($"[QuickSearchWindow] Drag effect failed: {ex.Message}", LogLevel.Error); }
        }
    }
}
