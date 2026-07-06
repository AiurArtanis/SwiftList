using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    // SizeToContent picks the initial size to match this plugin's actual schema (short schemas no
    // longer leave a large empty gap below their content), then this switches to Manual so the user
    // can still freely resize afterward -- SizeToContent and manual resizing can't be active at once.
    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        // Nested elements (e.g. each array field's detail panel, via ArrayDetailPanel_Loaded below)
        // can still be growing the content after this Loaded fires -- Loaded ordering between a
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

    // Brings the newly-added (and newly-selected) row into view when the master list has more
    // items than fit -- otherwise Add silently appends off-screen below the visible scroll area.
    private void ArrayMasterList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: not null } listBox)
            listBox.ScrollIntoView(listBox.SelectedItem);
    }

    // Keeps an array field's master list column exactly as tall as its detail panel, so only the
    // ListBox itself scrolls (not the whole page). Driven from code rather than a pure Grid Auto-row
    // + ElementName height binding, because that combination feeds back on itself: an unresolved
    // first-pass height lets the master list's own (unbounded) natural size leak into the row's Auto
    // height, which the detail panel -- if it were Stretch-aligned -- would then adopt too, permanently
    // locking in an inflated value. Doing the sync explicitly after each real layout avoids that.
    private void ArrayDetailPanel_Loaded(object sender, RoutedEventArgs e) => SyncArrayMasterListHeight(sender);

    private void ArrayDetailPanel_SizeChanged(object sender, SizeChangedEventArgs e) => SyncArrayMasterListHeight(sender);

    private static void SyncArrayMasterListHeight(object sender)
    {
        if (sender is not FrameworkElement detailPanel) return;
        if (VisualTreeHelper.GetParent(detailPanel) is not System.Windows.Controls.Grid parentGrid) return;

        var masterGrid = parentGrid.Children.OfType<System.Windows.Controls.Grid>()
            .FirstOrDefault(g => g.Name == "ArrayMasterListGrid");
        if (masterGrid == null) return;

        // An empty array has no selected item, so the detail panel collapses to zero height. Don't
        // mirror that onto the master list too -- that would hide the Add button along with it, which
        // is exactly what's needed to add the very first item. Fall back to its own natural size instead.
        masterGrid.Height = detailPanel.ActualHeight > 0 ? detailPanel.ActualHeight : double.NaN;
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
