using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SwiftList.App.ViewModels.Settings.Plugins;

namespace SwiftList.App.Views.Settings.Plugins;

public partial class PluginConfigWindow : Window
{
    public PluginConfigWindow(PluginInfoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    public bool IsSaved { get; private set; }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PluginInfoViewModel vm)
        {
            foreach (var field in vm.ConfigFields)
            {
                field.Commit();
            }
            if (vm.ConfigFields.Count > 0)
            {
                vm.ConfigFields[0].Settings.Save();
                App.HookClient?.SendMessage(new Core.IpcMessage { Id = Core.IpcMessageId.ReloadSettings });
            }
        }
        IsSaved = true;
        Close();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scrollViewer)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                if (e.Delta < 0)
                {
                    scrollViewer.LineRight();
                }
                else
                {
                    scrollViewer.LineLeft();
                }
                e.Handled = true;
            }
        }
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        if (sender is System.Windows.Controls.TextBox textBox)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.Clear || key == Key.OemClear)
            {
                return;
            }

            var parts = new List<string>();
            var modifiers = Keyboard.Modifiers;

            if (e.Key == Key.System)
            {
                modifiers |= ModifierKeys.Alt;
            }

            // If the field requires a modifier key, reject plain key presses
            var requireModifier = textBox.DataContext is PluginConfigFieldViewModel { HotkeyRequireModifier: true };
            if (requireModifier && modifiers == ModifierKeys.None)
                return;

            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            if (key == Key.Escape)
            {
                textBox.Text = string.Empty;
                var expression = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                expression?.UpdateSource();
                return;
            }

            parts.Add(key.ToString());
            var hotkeyStr = string.Join("+", parts);

            textBox.Text = hotkeyStr;
            var bindingExpr = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            bindingExpr?.UpdateSource();
        }
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var panel = btn.Parent as System.Windows.Controls.StackPanel;
        var textBox = panel?.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();

        if (btn.Tag as string == "Folder")
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() == true)
            {
                textBox!.Text = dlg.FolderName;
                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                textBox!.Text = dlg.FileName;
                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
        }
    }
}
