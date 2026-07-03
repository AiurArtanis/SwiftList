using System.Windows;
using System.Windows.Input;

namespace SwiftList.App.Views.Controls;

public partial class HotkeyRecorderControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(HotkeyRecorderControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyRecorderControl ctrl)
            ctrl.HotkeyBox.Text = e.NewValue as string ?? string.Empty;
    }

    public static readonly DependencyProperty RequireModifierProperty =
        DependencyProperty.Register(nameof(RequireModifier), typeof(bool),
            typeof(HotkeyRecorderControl), new PropertyMetadata(false));

    public bool RequireModifier
    {
        get => (bool)GetValue(RequireModifierProperty);
        set => SetValue(RequireModifierProperty, value);
    }

    public HotkeyRecorderControl() => InitializeComponent();

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.Clear or Key.OemClear)
            return;

        if (key == Key.Escape) { Value = string.Empty; return; }

        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.System) modifiers |= ModifierKeys.Alt;

        if (RequireModifier && modifiers == ModifierKeys.None) return;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        Value = string.Join("+", parts);
    }
}
