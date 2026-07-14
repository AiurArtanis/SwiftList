namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents the base interface for all plugin components.
/// </summary>
public interface IPluginComponent
{
    /// <summary>
    /// A stable, locale-independent identifier for this component.
    /// Defaults to the concrete type name.
    /// </summary>
    string Id => GetType().Name;

    /// <summary>
    /// The display name of this component.
    /// Defaults to the concrete type name.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// A short description of this component.
    /// </summary>
    string Description => string.Empty;
}
