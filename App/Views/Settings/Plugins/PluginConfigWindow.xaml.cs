using System.Windows;
using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings.Plugins;

using SwiftList.App.Services.Theme;
namespace SwiftList.App.Views.Settings.Plugins;

public partial class PluginConfigWindow : Window
{
    public PluginConfigWindow(PluginInfoViewModel viewModel)
    {
        InitializeComponent();
        Helpers.SystemMenuBlocker.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        DataContext = viewModel;
    }

    // SizeToContent picks the initial size to match this plugin's actual schema (short schemas no
    // longer leave a large empty gap below their content), then this switches to Manual so the user
    // can still freely resize afterward -- SizeToContent and manual resizing can't be active at once.
    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        // Nested elements (e.g. each array field's detail panel, via FieldRowTemplate.xaml.cs's
        // ArrayDetailPanel_Loaded) can still be growing the content after this Loaded fires -- Loaded ordering between a
        // window and its descendants isn't guaranteed. Defer to ContextIdle so every such handler,
        // and the layout pass each one triggers, has already settled before this window "freezes"
        // its size; freezing too early has locked in a height shorter than the real content.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            SizeToContent = SizeToContent.Manual;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;

            // Restore fill-remaining-space behavior for the content row now that SizeToContent is
            // done with it -- see the comment on ContentRow in the XAML for why it starts as Auto.
            ContentRow.Height = new GridLength(1, GridUnitType.Star);

            // WindowStartupLocation="CenterOwner" positions the window before SizeToContent has
            // finished resolving its real size, so it often centers using a stale/placeholder size.
            // Recenter now that ActualWidth/ActualHeight reflect the real, final size.
            if (Owner != null)
            {
                Left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
                Top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);

    public void ResizeToFit()
    {
        if (SizeToContent != SizeToContent.Manual) return;

        // Temporarily reset row height to Auto to avoid the WPF degenerate size-to-content case
        ContentRow.Height = GridLength.Auto;
        SizeToContent = SizeToContent.WidthAndHeight;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            SizeToContent = SizeToContent.Manual;
            ContentRow.Height = new GridLength(1, GridUnitType.Star);

            if (Owner != null)
            {
                Left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
                Top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
}
