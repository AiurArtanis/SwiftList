using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App;

// Title-bar search box + results popup for SettingsWindow, as extension methods (matching RuntimeIndex's
// BucketExtensions/QueryExtensions split) instead of an extra partial-class file, to stay under the
// file-length convention. SettingsWindow.xaml.cs itself must stay partial (the WPF/XAML tooling
// requires it), but this second file is not that generated half -- it's purely this session's own
// split, so it follows the same composition/extension-method pattern as everywhere else in the project.
// The three XAML-wired event handlers (TextChanged/PreviewKeyDown/MouseUp) stay as thin instance methods
// on SettingsWindow itself, since XAML event wiring resolves by reflection and can't target an
// extension method; everything they call into lives here.
internal static class SettingsWindowSearchExtensions
{
    public static void CloseSearchPopup(this SettingsWindow window) => window.SearchResultsPopup.IsOpen = false;

    // "Section", "Section > Tab", or "Section > Tab > SubTab" -- entries whose own LabelKey names a
    // tab/sub-tab leave TabLabelKey/SubTabLabelKey null (see SettingsSearchEntry), so their breadcrumb
    // stops at the parent instead of repeating the result's own label back at the user.
    // Internal, not private: also used by App.xaml.cs to build the entry list exposed to plugins via
    // PluginSdk.Services.SettingsSearchService.
    internal static string BuildBreadcrumb(SettingsSearchEntry entry)
    {
        var parts = new List<string> { TranslationManager.Instance[$"Settings_{entry.Section}"] };
        if (entry.TabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.TabLabelKey]);
        if (entry.SubTabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.SubTabLabelKey]);
        return string.Join(" › ", parts);
    }

    public static void OnSettingsSearchTextChanged(this SettingsWindow window)
    {
        var query = window.TxtSettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            window.CloseSearchPopup();
            return;
        }

        var results = new List<SettingsSearchResultItem>();
        var vm = window.DataContext as SettingsViewModel;
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            // Entries like the WSL tab are only reachable while their own section is actually shown
            // (IsVisible null for the overwhelming majority of entries, which are always reachable).
            // Without vm (DataContext not yet set) there's no way to evaluate the predicate, so such
            // an entry is conservatively excluded rather than shown as a dead link.
            if (entry.IsVisible != null && (vm == null || !entry.IsVisible(vm)))
                continue;

            var label = TranslationManager.Instance[entry.LabelKey];
            if (Core.FuzzyMatcher.IsMatch(query, label))
                results.Add(new SettingsSearchResultItem(label, BuildBreadcrumb(entry), entry.Section, entry.Activate, entry.TargetElementName));
        }

        // These three collections have no static Entries above -- their labels only exist at runtime
        // (whatever plugins happen to be loaded), so search the same live models each page renders from.
        if (vm != null)
        {
            var pluginsSectionLabel = TranslationManager.Instance["Settings_Plugins"];
            foreach (var plugin in vm.Plugins.Plugins)
            {
                var capturedPlugin = plugin;
                void ExpandPlugin(SettingsViewModel _) => capturedPlugin.IsExpanded = true;

                if (Core.FuzzyMatcher.IsMatch(query, plugin.Name))
                    results.Add(new SettingsSearchResultItem(plugin.Name, pluginsSectionLabel, "Plugins", ExpandPlugin,
                        Reveal: new SettingsSearchDynamicReveal("PluginsList", capturedPlugin)));

                foreach (var component in plugin.RawComponents)
                {
                    if (Core.FuzzyMatcher.IsMatch(query, component.DisplayName))
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

                if (Core.FuzzyMatcher.IsMatch(query, group.PluginName))
                    results.Add(new SettingsSearchResultItem(group.PluginName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel}", "Hotkeys", SelectPluginActionsTab,
                        Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup)));

                foreach (var action in group.Items)
                {
                    if (Core.FuzzyMatcher.IsMatch(query, action.DisplayName))
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

                if (Core.FuzzyMatcher.IsMatch(query, group.PluginName))
                    results.Add(new SettingsSearchResultItem(group.PluginName, $"{startupPanelSectionLabel} › {pluginTabsTabLabel}", "StartupPanel", SelectPluginTabsSubTab,
                        Reveal: new SettingsSearchDynamicReveal("PluginTabGroupsList", capturedGroup)));

                foreach (var tab in group.Tabs)
                {
                    if (Core.FuzzyMatcher.IsMatch(query, tab.Label))
                        results.Add(new SettingsSearchResultItem(tab.Label, $"{startupPanelSectionLabel} › {pluginTabsTabLabel} › {group.PluginName}", "StartupPanel", SelectPluginTabsSubTab,
                            Reveal: new SettingsSearchDynamicReveal("PluginTabGroupsList", capturedGroup, tab)));
                }
            }
        }

        window.LstSearchResults.ItemsSource = results;
        window.LstSearchResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        window.TxtSearchNoResults.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        window.SearchResultsPopup.IsOpen = true;
        // Highlights the top result so Enter picks it immediately, matching Windows 11 Settings search.
        // Doesn't navigate by itself -- only Enter/click (see ActivateSearchResult) commits a result.
        window.LstSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    // Wired to PreviewKeyDown, not KeyDown: the TextBox's default template hosts its text in a
    // ScrollViewer (PART_ContentHost), whose own class handler consumes Up/Down/PageUp/PageDown for
    // scrolling and marks them Handled before a bubbling KeyDown on the TextBox itself would ever see
    // them. Tunneling PreviewKeyDown fires first, top-down, so we get first refusal on those keys.
    public static void OnSettingsSearchKeyDown(this SettingsWindow window, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (window.LstSearchResults.SelectedItem is SettingsSearchResultItem item)
                window.ActivateSearchResult(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && window.LstSearchResults.Items.Count > 0)
        {
            // Wraps: Down past the last result loops back to the first.
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex + 1) % window.LstSearchResults.Items.Count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && window.LstSearchResults.Items.Count > 0)
        {
            // Wraps: Up past the first result loops back to the last.
            var count = window.LstSearchResults.Items.Count;
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex - 1 + count) % count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
            window.TxtSettingsSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    // Setting SelectedIndex from code doesn't scroll -- that only happens as a side effect of the
    // ListBox's own internal keyboard handling, which we bypass entirely (see OnSettingsSearchKeyDown
    // above; the ListBox itself never has focus).
    private static void ScrollSelectedResultIntoView(this SettingsWindow window)
    {
        if (window.LstSearchResults.SelectedItem != null)
            window.LstSearchResults.ScrollIntoView(window.LstSearchResults.SelectedItem);
    }

    // Deliberately not driven by SelectionChanged: SelectedIndex also changes for the "highlight the
    // top result" default and for Up/Down navigation, neither of which should navigate away. Mouse
    // clicks and Enter both funnel through here instead, only on an explicit commit.
    public static void OnSettingsSearchResultsMouseUp(this SettingsWindow window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is FrameworkElement { DataContext: SettingsSearchResultItem item })
            window.ActivateSearchResult(item);
    }

    // Internal, not private: also called directly by SettingsWindow.JumpToEntry (swiftlist://settings/
    // entry/<index>), which builds a SettingsSearchResultItem from a SettingsSearchIndex entry rather
    // than from a live text search.
    internal static void ActivateSearchResult(this SettingsWindow window, SettingsSearchResultItem item)
    {
        if (window.DataContext is SettingsViewModel vm)
            item.Activate?.Invoke(vm);

        window.SelectSection(item.Section);
        // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
        window.TxtSettingsSearch.Text = string.Empty;

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
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ResolveNamedElement(window.GetSectionPage(section), targetName) is FrameworkElement target)
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
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window.GetSectionPage(section)?.FindName(reveal.ListElementName) is not ItemsControl list
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

    private static FrameworkElement? GetSectionPage(this SettingsWindow window, string section) => section switch
    {
        "Service" => window.PageService,
        "Index" => window.PageIndex,
        "General" => window.PageGeneral,
        "Appearance" => window.PageAppearance,
        "Hotkeys" => window.PageHotkeys,
        "Plugins" => window.PagePlugins,
        "History" => window.PageHistory,
        "Favorites" => window.PageFavorites,
        "StartupPanel" => window.PageStartupPanel,
        "About" => window.PageAbout,
        _ => null,
    };
}
