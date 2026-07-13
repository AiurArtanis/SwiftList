namespace SwiftList.Core;

// Small settings-record types referenced from UserSettings, split out to keep that file under the
// project's line limit.

public class NetworkDriveSetting
{
    public string Id { get; set; } = string.Empty;
    public string RefreshMode { get; set; } = "Manual";
}

public class WslSetting
{
    public string Id { get; set; } = string.Empty; // e.g. "Ubuntu"
    public string RefreshMode { get; set; } = "Manual";
}

public class FolderIndexSetting
{
    public string Path { get; set; } = string.Empty; // the path itself is the identity, no separate Id
    public string RefreshMode { get; set; } = "Manual";
}

/// <summary>Everything shown on the Hotkey Settings page, grouped under one object.</summary>
public class HotkeyPageSettings
{
    /// <summary>
    /// A bare modifier (e.g. "Ctrl") means double-tap that modifier; a combo (e.g. "Alt+Space") means a
    /// literal key combination. See <see cref="HotkeyStringFormat"/>.
    /// </summary>
    public string ToggleWindowHotkey { get; set; } = "Ctrl";

    /// <summary>Same flat format as <see cref="ToggleWindowHotkey"/>.</summary>
    public string QuickSwitchHotkey { get; set; } = "Ctrl+G";

    public string SelectJumpModifier { get; set; } = "Ctrl";
    public string NextItemHotkey { get; set; } = "Ctrl+N";
    public string PreviousItemHotkey { get; set; } = "Ctrl+P";
    public string ActionsMenuHotkey { get; set; } = "Ctrl+O";
    public string CompleteFromSelectionHotkey { get; set; } = "Ctrl+Tab";
    public string QuickLookHotkey { get; set; } = "Alt+P";
    public bool QuickNavTriggerOnDoubleClick { get; set; } = true;
    public bool QuickNavTriggerOnMiddleClick { get; set; } = true;

    // Cycle back/forward through KeywordHistoryStore entries in the quick window's search box.
    public string KeywordHistoryPreviousHotkey { get; set; } = "Alt+Up";
    public string KeywordHistoryNextHotkey { get; set; } = "Alt+Down";

    // Deletes the keyword history entry currently shown in the search box (only while navigating
    // history via the two hotkeys above). A middle-click on the search box does the same thing and
    // isn't user-configurable, matching the always-on scroll-to-navigate gesture.
    public string KeywordHistoryDeleteHotkey { get; set; } = "Shift+Delete";

    // Cycles (wraps at both ends) through the Startup Panel's own tab strip -- see
    // StartupPanelController.SelectNextTab/SelectPreviousTab.
    public string StartupPanelNextTabHotkey { get; set; } = "Ctrl+Right";
    public string StartupPanelPreviousTabHotkey { get; set; } = "Ctrl+Left";

    /// <summary>
    /// User overrides for plugin action hotkeys, keyed by plugin ID (the DLL file name without its
    /// extension, matching <see cref="PluginSettings"/>'s convention) then by
    /// <c>ISearchResultAction.Id</c>. An empty string value means the action's hotkey is explicitly
    /// disabled; a missing entry (either level) means "use the action's own built-in default".
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> PluginActionHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FavoriteItemSetting
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class SearchWindowSettings
{
    public double SearchBarWidth { get; set; } = 570;
    public double SearchBarHeight { get; set; } = 55;
    public double CornerRadius { get; set; } = 8;
    // Base result-icon size for the quick window only (see UiMetrics); other windows use a fixed size.
    public double ResultIconSize { get; set; } = 55;
    public double? Left { get; set; }
    public double? Top { get; set; }
    // Floating date/time/day-of-week overlay above the quick window's search bar (see #101).
    public bool ShowClock { get; set; } = false;
}

public class PreviewWindowSettings
{
    // Defaults match the default search bar height (70) plus a fully-expanded 9-item results list
    // (9 * BaseSearchResultItemHeight = 459) -- see UiMetrics -- so the preview window's height is
    // predictable and doesn't change with however many results happen to be showing right now.
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 529;
}

/// <summary>The full/main SearchWindow's default size -- distinct from <see cref="SearchWindowSettings"/>,
/// which is the quick window's search bar layout. Updated automatically when the user drags the main
/// window's own resize grip, in addition to being editable on the General settings page.</summary>
public class MainWindowSettings
{
    public double Width { get; set; } = 854;
    public double Height { get; set; } = 480;
}
