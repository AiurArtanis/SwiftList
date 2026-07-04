using System.Windows;
using System.Windows.Media;

namespace SwiftList.App;

public class ActionMenuItem
{
    public string Text { get; set; } = string.Empty;
    public string SearchQuery { get; set; } = string.Empty;
    public uint CommandId { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsSectionHeader { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    public bool HasSubMenu { get; set; }
    public IntPtr SubMenuHandle { get; set; }
    public bool IsDisabled { get; set; }
    public ImageSource? Icon { get; set; }
    public string ShortcutHint { get; set; } = string.Empty;
    public Action? OnExecute { get; set; }

    public double ItemHeight { get; set; } = Services.UiMetrics.ListItemHeight;

    // Set for the quick-nav flyout so it renders at the compact shell-menu size (smaller font + shorter
    // rows) instead of the roomy full-window list size, while keeping the same layout and colors.
    public bool IsCompact { get; set; }

    // Base content sizes (match ActionMenuItem.xaml) and their scaled variants, used only by
    // the quick window so its action list scales with the configured search box height.
    private const double BaseIconSize = 16;
    private const double BaseTextFontSize = 13;
    private const double BaseSectionFontSize = 10;
    private const double BaseShortcutFontSize = 11;

    public double ScaledItemHeight => Math.Round(ItemHeight * Services.UiMetrics.Scale);
    public double ScaledIconSize => Math.Round(BaseIconSize * Services.UiMetrics.Scale);
    public double ScaledTextFontSize => Math.Round(BaseTextFontSize * Services.UiMetrics.Scale);
    public double ScaledSectionFontSize => Math.Round(BaseSectionFontSize * Services.UiMetrics.Scale);
    public double ScaledShortcutFontSize => Math.Round(BaseShortcutFontSize * Services.UiMetrics.Scale);

    public bool IsNormalItem => !IsSeparator && !IsSectionHeader;

    public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility => (Icon == null && !IsSeparator && !IsSectionHeader) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SectionHeaderVisibility => IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
}
