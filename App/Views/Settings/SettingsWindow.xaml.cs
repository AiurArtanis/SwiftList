using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;

using SwiftList.App.Services.ShellIcons;
using SwiftList.App.Services.Theme;
namespace SwiftList.App;

// Window chrome and the sidebar's own section-switching. Search box/popup logic lives in
// SettingsWindowSearchExtensions.cs (extension methods, kept separate to stay under the file-length
// convention) -- the three XAML-wired event handlers below stay here since XAML event wiring resolves
// by reflection and can't target an extension method.
public partial class SettingsWindow : Window
{
    private int _validationErrorCount;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        Helpers.SystemMenuBlocker.Attach(this);
        var vm = new SettingsViewModel();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            if (LstSections.SelectedItem == null && LstSectionsBottom.SelectedItem == null) LstSections.SelectedIndex = 0;
            FocusSearchBox();
        };
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
        Deactivated += (_, _) => this.CloseSearchPopup();
    }

    // Called on first Loaded, and again by AppWindowManager whenever an already-open (cached, just
    // hidden) window is re-shown -- Loaded only fires once per window lifetime, so that reuse path
    // wouldn't otherwise get focus back on the search box.
    public void FocusSearchBox() => Dispatcher.BeginInvoke(new Action(() =>
    {
        // Focus doesn't reliably stick if requested before the window has actually finished
        // activating -- deferring to Background priority (same pattern QuickSearchWindow uses for its
        // own search box) waits until layout/activation settles first.
        TxtSettingsSearch.Focus();
        Keyboard.Focus(TxtSettingsSearch);
    }), System.Windows.Threading.DispatcherPriority.Background);

    private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e) => this.OnSettingsSearchTextChanged();

    private void TxtSettingsSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => this.OnSettingsSearchKeyDown(e);

    private void LstSearchResults_MouseUp(object sender, MouseButtonEventArgs e) => this.OnSettingsSearchResultsMouseUp(e);

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

    // Case-insensitive: callers include swiftlist://settings/page/<section>, external/typed input that
    // shouldn't have to match this internal tag's exact casing.
    public void SelectSection(string tag)
    {
        if (LstSections == null || LstSectionsBottom == null)
            return;

        foreach (ListBoxItem item in LstSections.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LstSectionsBottom.SelectedItem = null;
                LstSections.SelectedItem = item;
                return;
            }
        }
        foreach (ListBoxItem item in LstSectionsBottom.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LstSections.SelectedItem = null;
                LstSectionsBottom.SelectedItem = item;
                return;
            }
        }
    }

    // swiftlist://settings/entry/<index> (see UriRouter) -- index is this entry's position in
    // SettingsSearchIndex.Entries, the same list PluginSdk.Services.SettingsSearchService exposes to
    // plugins, so a plugin-selected result round-trips straight back to the entry it matched. Reuses
    // ActivateSearchResult (the same jump+highlight a typed search-box match would trigger) rather than
    // duplicating its section/tab-selection and highlight logic.
    public void JumpToEntry(int index)
    {
        var entries = SettingsSearchIndex.Entries;
        if (index < 0 || index >= entries.Count)
            return;

        var entry = entries[index];
        var item = new SettingsSearchResultItem(
            TranslationManager.Instance[entry.LabelKey],
            SettingsWindowSearchExtensions.BuildBreadcrumb(entry),
            entry.Section,
            entry.Activate,
            entry.TargetElementName);
        this.ActivateSearchResult(item);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // The bottom-docked "About" entry lives in its own ListBox (see XAML comment on LstSectionsBottom) so
    // it can be pinned to the sidebar's bottom edge -- both lists feed the same page-switching logic below
    // and clear each other's selection so only one item is ever highlighted at a time.
    private void LstSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSections.SelectedItem == null)
            return;

        LstSectionsBottom.SelectedItem = null;
        ApplySelectedSection((LstSections.SelectedItem as ListBoxItem)?.Tag as string ?? "Service");
    }

    private void LstSectionsBottom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSectionsBottom.SelectedItem == null)
            return;

        LstSections.SelectedItem = null;
        ApplySelectedSection((LstSectionsBottom.SelectedItem as ListBoxItem)?.Tag as string ?? "About");
    }

    private void ApplySelectedSection(string tag)
    {
        if (PageIndex == null)
            return;

        // Covers navigating via the sidebar directly while a search popup happens to be open (typed a
        // query, then clicked a section instead of a result) -- clearing the text closes the popup too.
        TxtSettingsSearch.Text = string.Empty;

        PageService?.Visibility = tag == "Service" ? Visibility.Visible : Visibility.Collapsed;

        PageIndex.Visibility = tag == "Index" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PagePlugins.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        PageFavorites.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
        PageStartupPanel.Visibility = tag == "StartupPanel" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }
}
