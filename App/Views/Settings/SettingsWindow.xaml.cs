using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App;

public partial class SettingsWindow : Window
{
    private int _validationErrorCount;

    public SettingsWindow()
    {
        InitializeComponent();
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

        var tag = (LstSections.SelectedItem as ListBoxItem)?.Tag as string ?? "Service";

        PageService?.Visibility = tag == "Service" ? Visibility.Visible : Visibility.Collapsed;

        PageIndex.Visibility = tag == "Index" ? Visibility.Visible : Visibility.Collapsed;
        PageExclusions.Visibility = tag == "Exclusions" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PagePlugins.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        PageFavorites.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        PageBlacklist?.Visibility = tag == "Blacklist" ? Visibility.Visible : Visibility.Collapsed;
    }
}
