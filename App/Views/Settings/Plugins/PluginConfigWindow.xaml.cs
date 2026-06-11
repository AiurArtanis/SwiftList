using System.Windows;
using System.Windows.Input;
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
}
