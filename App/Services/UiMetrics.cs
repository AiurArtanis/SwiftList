using SwiftList.Core;

namespace SwiftList.App.Services;

public static class UiMetrics
{
    // ── Base (design) metrics, calibrated for the default search bar height ──
    public const double DefaultSearchBarHeight = 70;
    public const double BaseSearchResultItemHeight = 51;
    public const double BaseListItemHeight = 34;
    public const double BaseSearchSectionHeaderHeight = 28;

    // Floor for the action-menu section header row so its title font never gets clipped.
    public const double MinSectionHeaderHeight = 18;

    // The result row's ItemBorder (ResultItemStyle/ActionItemStyle in ListBox.xaml) has
    // Margin="6,2,6,2" -- since that Border is the template root, its 2px top + 2px bottom margin adds
    // to the row's own measured/desired size. Row-height math needs to budget for it, or a row whose
    // icon drives it past the base height still comes out a few pixels short of what actually renders.
    public const double ResultRowVerticalMargin = 4;

    // Base font/icon metrics used by the search result item template. Name:Path is weighted 8:5 (~60:40)
    // when both lines show, tilting more toward the name than an even split while keeping the path line
    // (the smaller of the two) comfortably legible.
    public const double BaseResultNameFontSize = 16;
    public const double BaseResultPathFontSize = 10;
    // A row with no path subtitle (applications, blank ParentDir) gives the whole name/path line-height
    // budget to the name alone instead of splitting it with an empty second line.
    public const double BaseResultNameFontSizeSingleLine = 20;
    public const double BaseResultIconSize = 42; // fixed size for the main window

    // Floors for the quick window's icon-relative font scaling (see ScaledResultNameFontSize etc.) --
    // at the smallest configurable icon size, the raw ratio would shrink text well past legible.
    public const double MinScaledResultNameFontSize = 12;
    public const double MinScaledResultPathFontSize = 9;
    public const double MinScaledResultNameFontSizeSingleLine = 14;

    // Fixed icon size for the inline window's own (more compact) row template
    // (App/Resources/DataTemplates/InlineSearchResult.xaml binds its Image to this, so the two never
    // drift apart the way the quick window's icon size and row height once did).
    public const double BaseInlineResultIconSize = 30;

    // Range for the user-configurable quick-window icon size setting (General settings page). 64 stays
    // well under the 96px IShellItemImageFactory fetch size real file icons use, so it's still crisp.
    public const double MinQuickResultIconSize = 16;
    public const double MaxQuickResultIconSize = 64;

    // Range for the user-configurable QuickLook preview window size (General settings page).
    public const double MinPreviewWindowWidth = 250;
    public const double MaxPreviewWindowWidth = 900;
    public const double MinPreviewWindowHeight = 250;
    public const double MaxPreviewWindowHeight = 1200;

    private static double _scale = 1.0;
    private static double _quickResultIconSize = BaseResultIconSize;
    private static double _previewWindowWidth = 400;
    private static double _previewWindowHeight = 529;

    /// <summary>
    /// Global UI scale factor. Result rows, fonts and icons multiply their
    /// base metrics by this value so they grow/shrink together with the
    /// user-configured search box height.
    /// </summary>
    public static double Scale
    {
        get => _scale;
        set => _scale = Math.Clamp(value, 0.6, 1.8);
    }

    /// <summary>
    /// Derives the scale factor from the configured search bar height so the
    /// result list scales proportionally (e.g. 70px -> 1.0, 105px -> 1.5).
    /// </summary>
    public static void UpdateScaleFromSearchBarHeight(double searchBarHeight)
    {
        if (searchBarHeight > 0)
            Scale = searchBarHeight / DefaultSearchBarHeight;
    }

    /// <summary>Base icon size for the quick window's ScaledResultIconSize, before the search-bar-height
    /// scale is applied. User-configurable (General settings page); clamped to a sane display range.</summary>
    public static double QuickResultIconSize
    {
        get => _quickResultIconSize;
        set => _quickResultIconSize = Math.Clamp(value, MinQuickResultIconSize, MaxQuickResultIconSize);
    }

    /// <summary>QuickLook preview window size. User-configurable (General settings page); fixed rather
    /// than derived from the owner window's current height so it doesn't change with however many
    /// results happen to be showing right now.</summary>
    public static double PreviewWindowWidth
    {
        get => _previewWindowWidth;
        set => _previewWindowWidth = Math.Clamp(value, MinPreviewWindowWidth, MaxPreviewWindowWidth);
    }

    public static double PreviewWindowHeight
    {
        get => _previewWindowHeight;
        set => _previewWindowHeight = Math.Clamp(value, MinPreviewWindowHeight, MaxPreviewWindowHeight);
    }

    /// <summary>Loads the current search bar height, quick-window icon size, and preview window size
    /// from settings and applies them.</summary>
    public static void ApplyScaleFromSettings()
    {
        var settings = UserSettings.Load();
        try { UpdateScaleFromSearchBarHeight(settings.SearchWindow.SearchBarHeight); }
        catch { /* fall back to current scale */ }
        try { QuickResultIconSize = settings.SearchWindow.ResultIconSize; }
        catch { /* fall back to current icon size */ }
        try { PreviewWindowWidth = settings.PreviewWindow.Width; }
        catch { /* fall back to current preview width */ }
        try { PreviewWindowHeight = settings.PreviewWindow.Height; }
        catch { /* fall back to current preview height */ }
    }

    // ── Base metrics (used everywhere by default: inline window, full window, action menu) ──
    public static double SearchResultItemHeight => BaseSearchResultItemHeight;
    public static double ListItemHeight => BaseListItemHeight;
    public static double SearchSectionHeaderHeight => BaseSearchSectionHeaderHeight;
    public static double MenuItemHeight => ListItemHeight * 0.8;

    public static double ResultNameFontSize => BaseResultNameFontSize;
    public static double ResultNameFontSizeSingleLine => BaseResultNameFontSizeSingleLine;
    public static double ResultPathFontSize => BaseResultPathFontSize;
    public static double ResultIconSize => BaseResultIconSize;
    public static double InlineResultIconSize => BaseInlineResultIconSize;

    // Design height for a compact inline row before accounting for its icon (see AppSearchResult.InlineItemHeight).
    public static double BaseInlineItemHeight => Math.Round(BaseSearchResultItemHeight * 0.7);

    // ── Scaled metrics — consumed ONLY by the quick window (opted in via window title),
    //    so the inline/full windows never scale with the search-bar height. ──
    public static double ScaledSearchResultItemHeight => Math.Round(BaseSearchResultItemHeight * _scale);
    public static double ScaledListItemHeight => Math.Round(BaseListItemHeight * _scale);
    public static double ScaledSearchSectionHeaderHeight => Math.Round(BaseSearchSectionHeaderHeight * _scale);

    public static double ScaledResultIconSize => Math.Round(_quickResultIconSize * _scale);

    // Name/path font size tracks the ACTUAL rendered icon size (not just the search-bar-height scale),
    // so bumping the icon-size setting directly grows the text too, not only bumping the search bar
    // height. At the default icon size (BaseResultIconSize) with no search-bar scaling this ratio is
    // exactly 1, so it's a no-op in the common case; floored so a small configured icon never shrinks
    // text past legible.
    private static double IconRelativeFontScale => ScaledResultIconSize / BaseResultIconSize;

    public static double ScaledResultNameFontSize => Math.Max(MinScaledResultNameFontSize, Math.Round(BaseResultNameFontSize * IconRelativeFontScale));
    public static double ScaledResultNameFontSizeSingleLine => Math.Max(MinScaledResultNameFontSizeSingleLine, Math.Round(BaseResultNameFontSizeSingleLine * IconRelativeFontScale));
    public static double ScaledResultPathFontSize => Math.Max(MinScaledResultPathFontSize, Math.Round(BaseResultPathFontSize * IconRelativeFontScale));
}
