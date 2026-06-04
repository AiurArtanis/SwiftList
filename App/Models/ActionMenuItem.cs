using System;
using System.Windows;
using System.Windows.Media;

namespace SwiftList.App
{
    public class ActionMenuItem
    {
        public string Text { get; set; } = string.Empty;
        public uint CommandId { get; set; }
        public bool IsSeparator { get; set; }
        public bool IsSectionHeader { get; set; }
        public string SectionTitle { get; set; } = string.Empty;
        public bool HasSubMenu { get; set; }
        public IntPtr SubMenuHandle { get; set; }
        public bool IsDisabled { get; set; }
        public ImageSource? Icon { get; set; }

        public bool IsNormalItem => !IsSeparator && !IsSectionHeader;

        public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => (Icon == null && !IsSeparator && !IsSectionHeader) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SectionHeaderVisibility => IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;
    }
}
