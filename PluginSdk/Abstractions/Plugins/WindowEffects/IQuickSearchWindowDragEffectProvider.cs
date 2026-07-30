using System.Windows;

namespace SwiftList.PluginSdk.Abstractions.Plugins.WindowEffects;

/// <summary>
/// Receives the quick-search window's user-initiated drag lifecycle.
/// Implementations may display transient visual effects and adjust the window position.
/// </summary>
public interface IQuickSearchWindowDragEffectProvider : IPluginComponent
{
    void OnDragStarted(Window window);
    /// <summary>Receives the current searchable card so effects can align to its visible bounds instead of the transparent window shadow.</summary>
    void OnDragMoved(Window window, FrameworkElement searchCard);
    void OnDragEnded(Window window);
}
