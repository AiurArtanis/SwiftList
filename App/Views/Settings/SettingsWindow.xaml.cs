using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App;

// Window chrome and the sidebar's own section-switching. Search box/popup logic lives in
// SettingsWindow.Search.cs (kept separate to stay under the file-length convention).
public partial class SettingsWindow : Window
{
    private int _validationErrorCount;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        var vm = new SettingsViewModel();
        DataContext = vm;
        Loaded += (_, _) => { if (LstSections.SelectedItem == null) LstSections.SelectedIndex = 0; };
        Closed += (_, _) =>
        {
            vm.Cleanup();
            // Release cached bitmaps and trim the working set on close, like the search windows.
            ShellIconHelper.ClearCache();
            Core.Win32Api.TrimWorkingSet();
        };
        this.AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(OnValidationError));
        // The popup is StaysOpen="True" (see its XAML comment), so it won't auto-close when the whole
        // window loses focus -- close it explicitly instead of leaving a stale flyout floating over
        // whatever the user alt-tabbed to.
        Deactivated += (_, _) => CloseSearchPopup();
    }

    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added)
            _validationErrorCount++;
        else
            _validationErrorCount--;

        if (DataContext is SettingsViewModel vm)
        {
            vm.CanApply = _validationErrorCount == 0;
        }
    }

    public void SelectSection(string tag)
    {
        if (LstSections == null)
            return;

        foreach (ListBoxItem item in LstSections.Items)
        {
            if (item.Tag as string == tag)
            {
                LstSections.SelectedItem = item;
                break;
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void LstSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageIndex == null)
            return;

        // Covers navigating via the sidebar directly while a search popup happens to be open (typed a
        // query, then clicked a section instead of a result) -- clearing the text closes the popup too.
        TxtSettingsSearch.Text = string.Empty;

        var tag = (LstSections.SelectedItem as ListBoxItem)?.Tag as string ?? "Service";

        PageService?.Visibility = tag == "Service" ? Visibility.Visible : Visibility.Collapsed;

        PageIndex.Visibility = tag == "Index" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PagePlugins.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        PageFavorites.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
        PageStartupPanel.Visibility = tag == "StartupPanel" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }
}
