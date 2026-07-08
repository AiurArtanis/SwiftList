using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App;

// Title-bar search box + results popup. Split from SettingsWindow.xaml.cs (window chrome, sidebar) to
// stay under the file-length convention.
public partial class SettingsWindow
{
    private void CloseSearchPopup() => SearchResultsPopup.IsOpen = false;

    // "Section", "Section > Tab", or "Section > Tab > SubTab" -- entries whose own LabelKey names a
    // tab/sub-tab leave TabLabelKey/SubTabLabelKey null (see SettingsSearchEntry), so their breadcrumb
    // stops at the parent instead of repeating the result's own label back at the user.
    private static string BuildBreadcrumb(SettingsSearchEntry entry)
    {
        var parts = new List<string> { TranslationManager.Instance[$"Settings_{entry.Section}"] };
        if (entry.TabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.TabLabelKey]);
        if (entry.SubTabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.SubTabLabelKey]);
        return string.Join(" › ", parts);
    }

    private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TxtSettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            CloseSearchPopup();
            return;
        }

        var results = new List<SettingsSearchResultItem>();
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            var label = TranslationManager.Instance[entry.LabelKey];
            if (label.Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add(new SettingsSearchResultItem(label, BuildBreadcrumb(entry), entry.Section, entry.Activate, entry.TargetElementName));
        }

        // These three collections have no static Entries above -- their labels only exist at runtime
        // (whatever plugins happen to be loaded), so search the same live models each page renders from.
        if (DataContext is SettingsViewModel vm)
        {
            var pluginsSectionLabel = TranslationManager.Instance["Settings_Plugins"];
            foreach (var plugin in vm.Plugins.Plugins)
            {
                var capturedPlugin = plugin;
                void ExpandPlugin(SettingsViewModel _) => capturedPlugin.IsExpanded = true;

                if (plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(new SettingsSearchResultItem(plugin.Name, pluginsSectionLabel, "Plugins", ExpandPlugin,
                        Reveal: new SettingsSearchDynamicReveal("PluginsList", capturedPlugin)));

                foreach (var component in plugin.RawComponents)
                {
                    if (component.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        results.Add(new SettingsSearchResultItem(component.DisplayName, $"{pluginsSectionLabel} › {plugin.Name}", "Plugins", ExpandPlugin,
                            Reveal: new SettingsSearchDynamicReveal("PluginsList", capturedPlugin, component)));
                }
            }

            var hotkeysSectionLabel = TranslationManager.Instance["Settings_Hotkeys"];
            var pluginActionsTabLabel = TranslationManager.Instance["Hotkeys_Tab_PluginActions"];
            foreach (var group in vm.Hotkeys.PluginActionGroups)
            {
                var capturedGroup = group;
                void SelectPluginActionsTab(SettingsViewModel v) => v.Hotkeys.SelectedTab = "PluginActions";

                if (group.PluginName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(new SettingsSearchResultItem(group.PluginName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel}", "Hotkeys", SelectPluginActionsTab,
                        Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup)));

                foreach (var action in group.Items)
                {
                    if (action.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        results.Add(new SettingsSearchResultItem(action.DisplayName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel} › {group.PluginName}", "Hotkeys", SelectPluginActionsTab,
                            Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup, action)));
                }
            }

            var startupPanelSectionLabel = TranslationManager.Instance["Settings_StartupPanel"];
            var pluginTabsTabLabel = TranslationManager.Instance["StartupPanel_TabPluginTabs"];
            foreach (var group in vm.StartupPanel.PluginTabGroups)
            {
                var capturedGroup = group;
                void SelectPluginTabsSubTab(SettingsViewModel v) => v.StartupPanel.SelectedSubTab = "PluginTabs";

                if (group.PluginName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(new SettingsSearchResultItem(group.PluginName, $"{startupPanelSectionLabel} › {pluginTabsTabLabel}", "StartupPanel", SelectPluginTabsSubTab,
                        Reveal: new SettingsSearchDynamicReveal("PluginTabGroupsList", capturedGroup)));

                foreach (var tab in group.Tabs)
                {
                    if (tab.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                        results.Add(new SettingsSearchResultItem(tab.Label, $"{startupPanelSectionLabel} › {pluginTabsTabLabel} › {group.PluginName}", "StartupPanel", SelectPluginTabsSubTab,
                            Reveal: new SettingsSearchDynamicReveal("PluginTabGroupsList", capturedGroup, tab)));
                }
            }
        }

        LstSearchResults.ItemsSource = results;
        LstSearchResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtSearchNoResults.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchResultsPopup.IsOpen = true;
        // Highlights the top result so Enter picks it immediately, matching Windows 11 Settings search.
        // Doesn't navigate by itself -- only Enter/click (see ActivateSearchResult) commits a result.
        LstSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    // Wired to PreviewKeyDown, not KeyDown: the TextBox's default template hosts its text in a
    // ScrollViewer (PART_ContentHost), whose own class handler consumes Up/Down/PageUp/PageDown for
    // scrolling and marks them Handled before a bubbling KeyDown on the TextBox itself would ever see
    // them. Tunneling PreviewKeyDown fires first, top-down, so we get first refusal on those keys.
    private void TxtSettingsSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (LstSearchResults.SelectedItem is SettingsSearchResultItem item)
                ActivateSearchResult(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && LstSearchResults.Items.Count > 0)
        {
            // Wraps: Down past the last result loops back to the first.
            LstSearchResults.SelectedIndex = (LstSearchResults.SelectedIndex + 1) % LstSearchResults.Items.Count;
            ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && LstSearchResults.Items.Count > 0)
        {
            // Wraps: Up past the first result loops back to the last.
            var count = LstSearchResults.Items.Count;
            LstSearchResults.SelectedIndex = (LstSearchResults.SelectedIndex - 1 + count) % count;
            ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
            TxtSettingsSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    // Setting SelectedIndex from code doesn't scroll -- that only happens as a side effect of the
    // ListBox's own internal keyboard handling, which we bypass entirely (see TxtSettingsSearch_KeyDown
    // above; the ListBox itself never has focus).
    private void ScrollSelectedResultIntoView()
    {
        if (LstSearchResults.SelectedItem != null)
            LstSearchResults.ScrollIntoView(LstSearchResults.SelectedItem);
    }

    // Deliberately not driven by SelectionChanged: SelectedIndex also changes for the "highlight the
    // top result" default and for Up/Down navigation, neither of which should navigate away. Mouse
    // clicks and Enter both funnel through here instead, only on an explicit commit.
    private void LstSearchResults_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is FrameworkElement { DataContext: SettingsSearchResultItem item })
            ActivateSearchResult(item);
    }

    private void ActivateSearchResult(SettingsSearchResultItem item)
    {
        if (DataContext is SettingsViewModel vm)
            item.Activate?.Invoke(vm);

        SelectSection(item.Section);
        // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
        TxtSettingsSearch.Text = string.Empty;

        // Switching section/tab alone doesn't reset scroll position -- a page's ScrollViewer just
        // clamps whatever offset it already had to the newly-visible content's (possibly shorter)
        // bounds, which can land anywhere rather than on the matched setting. Defer to ContextIdle so
        // the tab-switch layout pass (triggered by the DataTrigger bindings above) has already run;
        // BringIntoView walks up to whichever ancestor ScrollViewer actually owns the scrolling. The
        // highlight flash (see SettingsSearchHighlight) mirrors Windows 11 Settings' search behavior.
        if (item.TargetElementName != null)
        {
            var targetName = item.TargetElementName;
            var section = item.Section;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ResolveNamedElement(GetSectionPage(section), targetName) is FrameworkElement target)
                {
                    target.BringIntoView();
                    SettingsSearchHighlight.Show(target);
                }
            }), DispatcherPriority.ContextIdle);
        }
        else if (item.Reveal != null)
        {
            var reveal = item.Reveal;
            var section = item.Section;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GetSectionPage(section)?.FindName(reveal.ListElementName) is not ItemsControl list
                    || list.ItemContainerGenerator.ContainerFromItem(reveal.GroupItem) is not FrameworkElement groupContainer)
                    return;

                // The child row (e.g. a plugin component under its card, or a hotkey action under its
                // group) only exists once any Activate-triggered expansion (e.g. Plugins.IsExpanded) has
                // been measured/arranged -- that flip already happened synchronously above, but its
                // visual tree needs this same deferred pass to actually materialize.
                var target = reveal.ChildItem != null ? FindDescendantByDataContext(groupContainer, reveal.ChildItem) : null;
                target ??= groupContainer;
                target.BringIntoView();
                SettingsSearchHighlight.Show(target);
            }), DispatcherPriority.ContextIdle);
        }
    }

    // "TabSearchHistory/ChkEnable" resolves one FindName hop at a time -- the second segment names an
    // element declared inside HistoryListControl's own XAML, a separate NameScope from the settings
    // page hosting it, so the page's FindName can't reach it directly.
    private static FrameworkElement? ResolveNamedElement(FrameworkElement? root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('/'))
        {
            if (current?.FindName(segment) is not FrameworkElement next)
                return null;
            current = next;
        }
        return current;
    }

    private static FrameworkElement? FindDescendantByDataContext(DependencyObject root, object dataContext)
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } fe && ReferenceEquals(fe.DataContext, dataContext))
                return fe;

            if (FindDescendantByDataContext(child, dataContext) is FrameworkElement found)
                return found;
        }
        return null;
    }

    private FrameworkElement? GetSectionPage(string section) => section switch
    {
        "Service" => PageService,
        "Index" => PageIndex,
        "General" => PageGeneral,
        "Hotkeys" => PageHotkeys,
        "Plugins" => PagePlugins,
        "History" => PageHistory,
        "Favorites" => PageFavorites,
        "StartupPanel" => PageStartupPanel,
        "About" => PageAbout,
        _ => null,
    };
}
