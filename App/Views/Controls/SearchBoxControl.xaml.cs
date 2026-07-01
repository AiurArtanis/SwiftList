using System.Windows;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SwiftList.App;

public partial class SearchBoxControl : UserControl
{
    static SearchBoxControl()
    {
        PaddingProperty.OverrideMetadata(typeof(SearchBoxControl),
            new FrameworkPropertyMetadata(new Thickness(21, 19, 21, 19)));
        FontSizeProperty.OverrideMetadata(typeof(SearchBoxControl),
            new FrameworkPropertyMetadata(24.0));
    }

    public SearchBoxControl() => InitializeComponent();

    public TextBox SearchTextBox => TxtSearch;
    public TextBlock PlaceholderTextBlock => TxtPlaceholder;

    // SearchText DependencyProperty
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(SearchBoxControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    // IsIconOnLeft DependencyProperty
    public static readonly DependencyProperty IsIconOnLeftProperty = DependencyProperty.Register(
        nameof(IsIconOnLeft), typeof(bool), typeof(SearchBoxControl),
        new PropertyMetadata(false));

    public bool IsIconOnLeft
    {
        get => (bool)GetValue(IsIconOnLeftProperty);
        set => SetValue(IsIconOnLeftProperty, value);
    }

    // IsInActionsMode DependencyProperty
    public static readonly DependencyProperty IsInActionsModeProperty = DependencyProperty.Register(
        nameof(IsInActionsMode), typeof(bool), typeof(SearchBoxControl),
        new PropertyMetadata(false));

    public bool IsInActionsMode
    {
        get => (bool)GetValue(IsInActionsModeProperty);
        set => SetValue(IsInActionsModeProperty, value);
    }

}
