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

    // Base font/icon metrics used by the search result item template.
    public const double BaseResultNameFontSize = 14;
    public const double BaseResultPathFontSize = 11;
    public const double BaseResultIconSize = 42; // 28 + 50%

    private static double _scale = 1.0;

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

    /// <summary>Loads the current search bar height from settings and applies it.</summary>
    public static void ApplyScaleFromSettings()
    {
        try { UpdateScaleFromSearchBarHeight(UserSettings.Load().SearchWindow.SearchBarHeight); }
        catch { /* fall back to current scale */ }
    }

    // ── Base metrics (used everywhere by default: inline window, full window, action menu) ──
    public static double SearchResultItemHeight => BaseSearchResultItemHeight;
    public static double ListItemHeight => BaseListItemHeight;
    public static double SearchSectionHeaderHeight => BaseSearchSectionHeaderHeight;
    public static double MenuItemHeight => ListItemHeight * 0.8;

    public static double ResultNameFontSize => BaseResultNameFontSize;
    public static double ResultPathFontSize => BaseResultPathFontSize;
    public static double ResultIconSize => BaseResultIconSize;

    // ── Scaled metrics — consumed ONLY by the quick window (opted in via window title),
    //    so the inline/full windows never scale with the search-bar height. ──
    public static double ScaledSearchResultItemHeight => Math.Round(BaseSearchResultItemHeight * _scale);
    public static double ScaledListItemHeight => Math.Round(BaseListItemHeight * _scale);
    public static double ScaledSearchSectionHeaderHeight => Math.Round(BaseSearchSectionHeaderHeight * _scale);

    public static double ScaledResultNameFontSize => Math.Round(BaseResultNameFontSize * _scale);
    public static double ScaledResultPathFontSize => Math.Round(BaseResultPathFontSize * _scale);
    public static double ScaledResultIconSize => Math.Round(BaseResultIconSize * _scale);
}
