using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings.Plugins;

// Alias to avoid ambiguity with System.Drawing.Color
using WpfColor = System.Windows.Media.Color;

namespace SwiftList.App.Views.Converters;

/// <summary>Converts bool to Visibility (True → Visible, False → Collapsed).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>When true, inverts the conversion (False → Visible).</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is bool b && b;
        if (Invert || parameter as string == "Invert")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Converts a plugin component type enum to its localized label.</summary>
public class ComponentTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PluginComponentType type)
            return string.Empty;

        return TranslationManager.Instance[$"Plugins_Type{type}"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a plugin component type enum to a SolidColorBrush for UI badging.</summary>
public class ComponentTypeToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PluginComponentType type)
            return new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80));

        return type switch
        {
            PluginComponentType.Action => new SolidColorBrush(WpfColor.FromRgb(0x3B, 0x82, 0xF6)),
            PluginComponentType.DynamicActionProvider => new SolidColorBrush(WpfColor.FromRgb(0x8B, 0x5C, 0xF6)),
            PluginComponentType.InstantProvider => new SolidColorBrush(WpfColor.FromRgb(0x10, 0xB9, 0x81)),
            PluginComponentType.SearchableItemProvider => new SolidColorBrush(WpfColor.FromRgb(0xD9, 0x46, 0xEF)),
            PluginComponentType.FilterProvider => new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x9E, 0x0B)),
            PluginComponentType.ColumnProvider => new SolidColorBrush(WpfColor.FromRgb(0x63, 0x66, 0xF1)),
            PluginComponentType.AliasProvider => new SolidColorBrush(WpfColor.FromRgb(0xEC, 0x48, 0x99)),
            PluginComponentType.ActivePathCollector => new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x94, 0x88)),
            PluginComponentType.FileDialogAdapter => new SolidColorBrush(WpfColor.FromRgb(0xF9, 0x73, 0x16)),
            PluginComponentType.InlineSearchAdapter => new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x44, 0x44)),
            PluginComponentType.FilePreviewProvider => new SolidColorBrush(WpfColor.FromRgb(0x14, 0xB8, 0xA6)),
            PluginComponentType.QuickNavigationProvider => new SolidColorBrush(WpfColor.FromRgb(0x0E, 0x74, 0x90)),
            PluginComponentType.ThumbnailProvider => new SolidColorBrush(WpfColor.FromRgb(0x06, 0xB6, 0xD4)),
            PluginComponentType.QueryTokenProvider => new SolidColorBrush(WpfColor.FromRgb(0x84, 0xCC, 0x16)),
            PluginComponentType.StartupPanelTabProvider => new SolidColorBrush(WpfColor.FromRgb(0x0E, 0xA5, 0xE9)),
            _ => new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a string to Visibility (non-empty -> Visible, empty/null -> Collapsed).</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasText = value is string s && !string.IsNullOrWhiteSpace(s);
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts any reference to Visibility (non-null -> Visible, null -> Collapsed).</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts an empty/whitespace string to a localized "untitled" placeholder.</summary>
public class EmptyStringToPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? TranslationManager.Instance["Plugins_Config_UntitledItem"] : text!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts an array item's badge field VM to Visibility: Collapsed when the field is
/// absent (no second Text sub-field in the schema) or its value is an empty/whitespace string.
/// Binding straight to "BadgeField.Value" breaks the path (and silently falls back to the
/// default Visible) whenever BadgeField itself is null, so this converts on the field object.</summary>
public class BadgeFieldToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value as PluginConfigFieldViewModel)?.Value as string;
        return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Reference-equality check for two bound values, used to highlight the active tab when
/// tab identity is a live object (e.g. the selected plugin config Group) rather than a fixed string key.</summary>
public class ReferenceEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>True if any bound value is a bool true, used to OR together multiple conditions that
/// should independently keep a single DataTrigger active (e.g. "mouse over OR menu open").</summary>
public class BooleanOrConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Any(v => v is bool b && b);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
