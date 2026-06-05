using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.App.ViewModels.Settings.Plugins;

// Alias to avoid ambiguity with System.Drawing.Color
using WpfColor = System.Windows.Media.Color;

namespace SwiftList.App.Views.Converters
{
    /// <summary>Converts bool to Visibility (True → Visible, False → Collapsed).</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>When true, inverts the conversion (False → Visible).</summary>
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is bool b && b;
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
                PluginComponentType.Action              => new SolidColorBrush(WpfColor.FromRgb(0x3B, 0x82, 0xF6)),
                PluginComponentType.DynamicProvider     => new SolidColorBrush(WpfColor.FromRgb(0x8B, 0x5C, 0xF6)),
                PluginComponentType.InstantProvider     => new SolidColorBrush(WpfColor.FromRgb(0x10, 0xB9, 0x81)),
                PluginComponentType.FilterProvider      => new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x9E, 0x0B)),
                PluginComponentType.ColumnProvider      => new SolidColorBrush(WpfColor.FromRgb(0x63, 0x66, 0xF1)),
                PluginComponentType.AliasProvider       => new SolidColorBrush(WpfColor.FromRgb(0xEC, 0x48, 0x99)),
                PluginComponentType.ActivePathCollector => new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x94, 0x88)),
                PluginComponentType.FileDialogAdapter   => new SolidColorBrush(WpfColor.FromRgb(0xF9, 0x73, 0x16)),
                _                                       => new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x72, 0x80))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
