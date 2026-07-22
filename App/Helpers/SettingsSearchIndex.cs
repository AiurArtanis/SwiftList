using SwiftList.App.ViewModels.Settings;

namespace SwiftList.App.Helpers;

// One entry per (translated label -> where it lives). Activate optionally selects the tab/sub-tab
// that label belongs to before SettingsWindow switches sections. TargetElementName is the x:Name of
// the specific row control -- or, for a nested control inside another named control's own XAML (e.g.
// a HistoryListControl instance), an "outer/inner" path resolved by SettingsWindow.xaml.cs one FindName
// hop at a time. TabLabelKey/SubTabLabelKey are the ANCESTOR tab/group chain shown as the result's
// breadcrumb, e.g. "Index > Network Drives" -- left null when the entry itself names that tab/group
// (no point breadcrumbing a result to itself), set otherwise so same-named results in different tabs
// (e.g. "Rebuild Index" under both Local and Network Drives) stay distinguishable. IsVisible is for
// the rare entry whose own tab/row is conditionally hidden in the XAML (e.g. the WSL tab, only shown
// once a distribution is detected) -- left null for the overwhelming majority of entries that are
// always reachable, so a search result never points at a control the user can't actually see right
// now. Hand-curated -- there's no data-driven model of "every settings control" to generate this
// from (each page is hand-written XAML), so a newly added setting needs its own line here (and its
// own x:Name in the page's XAML) to become searchable.
public sealed record SettingsSearchEntry(
    string LabelKey,
    string Section,
    Action<SettingsViewModel>? Activate = null,
    string? TargetElementName = null,
    string? TabLabelKey = null,
    string? SubTabLabelKey = null,
    Func<SettingsViewModel, bool>? IsVisible = null);

public static class SettingsSearchIndex
{
    public static IReadOnlyList<SettingsSearchEntry> Entries { get; } = new List<SettingsSearchEntry>
    {
        // Service Status -- not wrapped in a ScrollViewer, so BringIntoView is a no-op; the row anchors
        // still drive the highlight flash.
        new("Settings_Service", "Service"),
        new("Service_Title", "Service"),
        new("Service_ActionInstall", "Service", TargetElementName: "RowActionInstall"),
        new("Service_ClearLog", "Service", TargetElementName: "RowClearLog"),
        new("Service_LogTab_App", "Service", vm => vm.Log.SelectedTab = "App"),
        new("Service_LogTab_Hook", "Service", vm => vm.Log.SelectedTab = "Hook"),
        new("Service_LogTab_Service", "Service", vm => vm.Log.SelectedTab = "Service"),

        // Index
        new("Settings_Index", "Index"),
        new("Settings_LocalDrive", "Index", vm => vm.LocalDrive.SelectedTab = "Local", "TabLocal"),
        new("Local_IndexStatus", "Index", vm => vm.LocalDrive.SelectedTab = "Local", "TabLocal/RowLocalRebuild", "Settings_LocalDrive"),
        new("Local_RebuildBtn", "Index", vm => vm.LocalDrive.SelectedTab = "Local", "TabLocal/RowLocalRebuild", "Settings_LocalDrive"),
        new("Settings_NetworkDrive", "Index", vm => vm.LocalDrive.SelectedTab = "Network", "TabNetwork"),
        new("Network_IndexStatus", "Index", vm => vm.LocalDrive.SelectedTab = "Network", "TabNetwork/RowNetworkRebuild", "Settings_NetworkDrive"),
        new("Network_RebuildBtn", "Index", vm => vm.LocalDrive.SelectedTab = "Network", "TabNetwork/RowNetworkRebuild", "Settings_NetworkDrive"),
        new("Network_WslSectionTitle", "Index", vm => vm.LocalDrive.SelectedTab = "Wsl", "TabWsl",
            IsVisible: vm => vm.NetworkDrive.IsWslPanelVisible),
        new("Network_IndexStatus", "Index", vm => vm.LocalDrive.SelectedTab = "Wsl", "TabWsl/RowWslRebuild", "Network_WslSectionTitle",
            IsVisible: vm => vm.NetworkDrive.IsWslPanelVisible),
        new("Network_RebuildBtn", "Index", vm => vm.LocalDrive.SelectedTab = "Wsl", "TabWsl/RowWslRebuild", "Network_WslSectionTitle",
            IsVisible: vm => vm.NetworkDrive.IsWslPanelVisible),
        new("Settings_FolderIndex", "Index", vm => vm.LocalDrive.SelectedTab = "Folders", "TabFolders"),
        new("Network_IndexStatus", "Index", vm => vm.LocalDrive.SelectedTab = "Folders", "TabFolders/RowFolderRebuild", "Settings_FolderIndex"),
        new("Network_RebuildBtn", "Index", vm => vm.LocalDrive.SelectedTab = "Folders", "TabFolders/RowFolderRebuild", "Settings_FolderIndex"),
        new("Folder_AddBtn", "Index", vm => vm.LocalDrive.SelectedTab = "Folders", "TabFolders/RowFolderRebuild", "Settings_FolderIndex"),
        new("Settings_Exclusions", "Index", vm => { vm.LocalDrive.SelectedTab = "Exclusions"; vm.Exclusions.SelectedSubTab = "Path"; }, "TabExclusions/SubTabExclusionsPath"),
        new("Exclusions_TabPath", "Index", vm => { vm.LocalDrive.SelectedTab = "Exclusions"; vm.Exclusions.SelectedSubTab = "Path"; }, "TabExclusions/SubTabExclusionsPath", "Settings_Exclusions"),
        new("Exclusions_TabGlob", "Index", vm => { vm.LocalDrive.SelectedTab = "Exclusions"; vm.Exclusions.SelectedSubTab = "Glob"; }, "TabExclusions/SubTabExclusionsGlob", "Settings_Exclusions"),
        new("Exclusions_TabRegex", "Index", vm => { vm.LocalDrive.SelectedTab = "Exclusions"; vm.Exclusions.SelectedSubTab = "Regex"; }, "TabExclusions/SubTabExclusionsRegex", "Settings_Exclusions"),

        // General
        new("Settings_General", "General"),
        new("General_SysTitle", "General", vm => vm.General.SelectedTab = "System", "TabSystem"),
        new("General_Startup", "General", vm => vm.General.SelectedTab = "System", "RowStartup", "General_SysTitle"),
        new("General_AutoCheckUpdates", "General", vm => vm.General.SelectedTab = "System", "RowAutoCheckUpdates", "General_SysTitle"),
        new("General_AutoSilentUpdate", "General", vm => vm.General.SelectedTab = "System", "RowAutoSilentUpdate", "General_SysTitle"),
        new("General_HardwareAcceleration", "General", vm => vm.General.SelectedTab = "System", "RowHardwareAcceleration", "General_SysTitle"),
        new("General_HideTrayIcon", "General", vm => vm.General.SelectedTab = "System", "RowHideTrayIcon", "General_SysTitle"),
        new("General_LogLevel", "General", vm => vm.General.SelectedTab = "System", "RowLogLevel", "General_SysTitle"),
        new("General_LangSelect", "General", vm => vm.General.SelectedTab = "System", "RowLangSelect", "General_SysTitle"),
        new("General_LayoutTitle", "General", vm => vm.General.SelectedTab = "Layout", "TabLayout"),
        new("General_LayoutWidth", "General", vm => vm.General.SelectedTab = "Layout", "RowLayoutWidth", "General_LayoutTitle"),
        new("General_LayoutHeight", "General", vm => vm.General.SelectedTab = "Layout", "RowLayoutHeight", "General_LayoutTitle"),
        new("General_LayoutShowClock", "General", vm => vm.General.SelectedTab = "Layout", "RowLayoutShowClock", "General_LayoutTitle"),
        new("General_LayoutReset", "General", vm => vm.General.SelectedTab = "Layout", "RowLayoutReset", "General_LayoutTitle"),
        new("General_PreviewWindowTitle", "General", vm => vm.General.SelectedTab = "PreviewWindow", "TabPreviewWindow"),
        new("General_PreviewWindowWidth", "General", vm => vm.General.SelectedTab = "PreviewWindow", "RowPreviewWindowWidth", "General_PreviewWindowTitle"),
        new("General_PreviewWindowHeight", "General", vm => vm.General.SelectedTab = "PreviewWindow", "RowPreviewWindowHeight", "General_PreviewWindowTitle"),
        new("General_PreviewWindowReset", "General", vm => vm.General.SelectedTab = "PreviewWindow", "RowPreviewWindowReset", "General_PreviewWindowTitle"),
        new("General_SearchWindowTitle", "General", vm => vm.General.SelectedTab = "SearchWindow", "TabSearchWindow"),
        new("General_SearchWindowWidth", "General", vm => vm.General.SelectedTab = "SearchWindow", "RowSearchWindowWidth", "General_SearchWindowTitle"),
        new("General_SearchWindowHeight", "General", vm => vm.General.SelectedTab = "SearchWindow", "RowSearchWindowHeight", "General_SearchWindowTitle"),
        new("General_SearchWindowReset", "General", vm => vm.General.SelectedTab = "SearchWindow", "RowSearchWindowReset", "General_SearchWindowTitle"),
        new("General_QuickNavTitle", "General", vm => vm.General.SelectedTab = "QuickNavigation", "TabQuickNavigation"),

        // Appearance
        new("Settings_Appearance", "Appearance"),
        new("Appearance_ModeGroupTitle", "Appearance", TargetElementName: "RowThemeModeCards"),
        new("Appearance_ModeLight", "Appearance", TargetElementName: "RowThemeModeCards", TabLabelKey: "Appearance_ModeGroupTitle"),
        new("Appearance_ModeDark", "Appearance", TargetElementName: "RowThemeModeCards", TabLabelKey: "Appearance_ModeGroupTitle"),
        new("Appearance_ModeFollowSystem", "Appearance", TargetElementName: "RowThemeModeCards", TabLabelKey: "Appearance_ModeGroupTitle"),
        new("General_ThemeSelect", "Appearance", TargetElementName: "RowThemeCards",
            IsVisible: vm => vm.Appearance.IsManualThemeEnabled),
        new("General_ThemeLightSelect", "Appearance", TargetElementName: "RowThemeLightCards",
            IsVisible: vm => vm.Appearance.FollowSystem),
        new("General_ThemeDarkSelect", "Appearance", TargetElementName: "RowThemeDarkCards",
            IsVisible: vm => vm.Appearance.FollowSystem),

        // Hotkeys
        new("Settings_Hotkeys", "Hotkeys"),
        new("Hotkeys_Tab_Global", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "TabGlobal"),
        new("Hotkeys_GroupGlobal", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowGroupGlobal", "Hotkeys_Tab_Global"),
        new("Hotkeys_ToggleLabel", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowToggleHotkey", "Hotkeys_Tab_Global"),
        new("Hotkeys_AllowInFullscreen", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowToggleHotkey", "Hotkeys_Tab_Global"),
        new("Hotkeys_QuickSwitchLabel", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowQuickSwitch", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectionTitle", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectionTitle", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectNextItem", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectNext", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectPreviousItem", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectPrevious", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectJumpItem", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectJump", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectActions", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectActions", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectComplete", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectComplete", "Hotkeys_Tab_Global"),
        new("Hotkeys_SelectQuickLook", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowSelectQuickLook", "Hotkeys_Tab_Global"),
        new("Hotkeys_KeywordHistoryPrevious", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowKeywordHistoryPrevious", "Hotkeys_Tab_Global"),
        new("Hotkeys_KeywordHistoryNext", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowKeywordHistoryNext", "Hotkeys_Tab_Global"),
        new("Hotkeys_KeywordHistoryDelete", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowKeywordHistoryDelete", "Hotkeys_Tab_Global"),
        new("Hotkeys_OpenFullWindow", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowOpenFullWindow", "Hotkeys_Tab_Global"),
        new("Hotkeys_GroupQuickNav", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowGroupQuickNav", "Hotkeys_Tab_Global"),
        new("Hotkeys_QuickNavDoubleClick", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowQuickNavDoubleClick", "Hotkeys_Tab_Global"),
        new("Hotkeys_QuickNavMiddleClick", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowQuickNavMiddleClick", "Hotkeys_Tab_Global"),
        new("Hotkeys_GroupStartupPanel", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowGroupStartupPanel", "Hotkeys_Tab_Global"),
        new("Hotkeys_StartupPanelNextTab", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowStartupPanelNextTab", "Hotkeys_Tab_Global"),
        new("Hotkeys_StartupPanelPreviousTab", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Global", "RowStartupPanelPreviousTab", "Hotkeys_Tab_Global"),
        new("Hotkeys_Tab_PluginActions", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "PluginActions", "TabPluginActions"),
        new("Settings_Blacklist", "Hotkeys", vm => vm.Hotkeys.SelectedTab = "Blacklist", "TabBlacklist"),

        // Plugins (component-level results come from the live Plugins model, see SettingsWindow.xaml.cs)
        new("Settings_Plugins", "Plugins"),

        // Favorites
        new("Settings_Favorites", "Favorites"),
        new("Favorites_Title", "Favorites"),
        new("Favorites_AddCardTitle", "Favorites", TargetElementName: "RowAddCardTitle"),
        new("Favorites_FieldName", "Favorites", TargetElementName: "RowFieldName", TabLabelKey: "Favorites_AddCardTitle"),
        new("Favorites_FieldPath", "Favorites", TargetElementName: "RowFieldPath", TabLabelKey: "Favorites_AddCardTitle"),
        new("Favorites_ListTitle", "Favorites", TargetElementName: "RowListTitle"),

        // History -- Enable/Clear All live inside the shared HistoryListControl, instantiated once per
        // tab; "Outer/Inner" targets one extra FindName hop into that instance's own XAML NameScope.
        new("Settings_History", "History"),
        new("Settings_History_Tab_Search", "History", vm => vm.History.SelectedTab = "Search", "TabSearchHistory"),
        new("Settings_History_Enable", "History", vm => vm.History.SelectedTab = "Search", "TabSearchHistory/ChkEnable", "Settings_History_Tab_Search"),
        new("Settings_History_Clear_All", "History", vm => vm.History.SelectedTab = "Search", "TabSearchHistory/BtnClearAll", "Settings_History_Tab_Search"),
        new("Settings_History_Tab_Keyword", "History", vm => vm.History.SelectedTab = "Keyword", "TabKeywordHistory"),
        new("Settings_History_Enable", "History", vm => vm.History.SelectedTab = "Keyword", "TabKeywordHistory/ChkEnable", "Settings_History_Tab_Keyword"),
        new("Settings_History_Clear_All", "History", vm => vm.History.SelectedTab = "Keyword", "TabKeywordHistory/BtnClearAll", "Settings_History_Tab_Keyword"),

        // Startup Panel
        new("Settings_StartupPanel", "StartupPanel"),
        new("StartupPanel_Enabled", "StartupPanel", TargetElementName: "RowStartupPanelEnabled"),
        new("StartupPanel_TabRecentFiles", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "RecentFiles", "SubTabRecentFiles"),
        new("StartupPanel_RecentFilesEnabled", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "RecentFiles", "RowRecentFilesEnabled", "StartupPanel_TabRecentFiles"),
        new("StartupPanel_RecentFilesDirectoriesDesc", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "RecentFiles", "RowRecentFilesDirectories", "StartupPanel_TabRecentFiles"),
        new("StartupPanel_RecentFilesCountDesc", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "RecentFiles", "RowRecentFilesCount", "StartupPanel_TabRecentFiles"),
        new("StartupPanel_RecentFilesMaxAgeDesc", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "RecentFiles", "RowRecentFilesMaxAge", "StartupPanel_TabRecentFiles"),
        new("StartupPanel_TabLastDirectory", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "LastDirectory", "SubTabLastDirectory"),
        new("StartupPanel_LastDirectoryEnabled", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "LastDirectory", "RowLastDirectoryEnabled", "StartupPanel_TabLastDirectory"),
        new("StartupPanel_TabPluginTabs", "StartupPanel", vm => vm.StartupPanel.SelectedSubTab = "PluginTabs", "SubTabPluginTabs"),

        // About
        new("Settings_About", "About"),
        new("About_CheckUpdate", "About", TargetElementName: "BtnCheckUpdate"),
    };
}

// A single row shown in the search results list -- either a static Entries match or a live match
// against a plugin-populated collection (Plugins' own components, Hotkeys' Plugin Actions tab, Startup
// Panel's Plugin Tabs sub-tab -- see SettingsSearchDynamicReveal). SectionLabel is the fully-composed,
// already-translated breadcrumb ("Section", "Section > Tab", or "Section > Tab > SubTab") shown as the
// row's subtitle.
public sealed record SettingsSearchResultItem(
    string Label,
    string SectionLabel,
    string Section,
    Action<SettingsViewModel>? Activate,
    string? TargetElementName = null,
    SettingsSearchDynamicReveal? Reveal = null);

// Locates a runtime list item that has no named XAML element of its own (unlike TargetElementName):
// ListElementName is the x:Name of the outer ItemsControl (e.g. "PluginsList",
// "PluginActionGroupsList", "PluginTabGroupsList"); GroupItem is the group-level view model whose
// container ItemContainerGenerator.ContainerFromItem locates; ChildItem, when set, narrows further to
// one row within that group's own visual tree (found by walking for a descendant whose DataContext is
// this instance) -- left null to reveal the whole group instead of one specific row inside it.
public sealed record SettingsSearchDynamicReveal(string ListElementName, object GroupItem, object? ChildItem = null);
