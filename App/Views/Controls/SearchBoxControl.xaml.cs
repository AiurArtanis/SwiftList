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

    public SearchBoxControl()
    {
        InitializeComponent();
        SizeChanged += SearchBoxControl_SizeChanged;
    }

    private void SearchBoxControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsDynamicScalingEnabled) return;

        var height = e.NewSize.Height;
        if (double.IsNaN(height) || height <= 0) return;

        // Dynamic Sizing:
        // FontSize = height * 0.34 (e.g. 70px -> 23.8px, 50px -> 17px)
        FontSize = Math.Clamp(height * 0.34, 12.0, 36.0);

        // Vertical Padding:
        // Keep horizontal padding fixed at 21, scale vertical padding dynamically (e.g. 70px -> 18.2px, 50px -> 13px)
        var verticalPadding = Math.Clamp(height * 0.26, 4.0, 30.0);
        Padding = new Thickness(21, verticalPadding, 21, verticalPadding);

        // Scale Left and Right icon sizes (e.g. Left: 70px -> 18.2px, Right: 70px -> 27.3px)
        LeftIconSize = Math.Clamp(height * 0.26, 10.0, 30.0);
        RightIconSize = Math.Clamp(height * 0.39, 15.0, 45.0);
    }

    public TextBox SearchTextBox => TxtSearch;
    public TextBlock PlaceholderTextBlock => TxtPlaceholder;

    // IsDynamicScalingEnabled DependencyProperty
    public static readonly DependencyProperty IsDynamicScalingEnabledProperty = DependencyProperty.Register(
        nameof(IsDynamicScalingEnabled), typeof(bool), typeof(SearchBoxControl),
        new PropertyMetadata(false));

    public bool IsDynamicScalingEnabled
    {
        get => (bool)GetValue(IsDynamicScalingEnabledProperty);
        set => SetValue(IsDynamicScalingEnabledProperty, value);
    }

    // LeftIconSize DependencyProperty
    public static readonly DependencyProperty LeftIconSizeProperty = DependencyProperty.Register(
        nameof(LeftIconSize), typeof(double), typeof(SearchBoxControl), new PropertyMetadata(18.0));

    public double LeftIconSize
    {
        get => (double)GetValue(LeftIconSizeProperty);
        set => SetValue(LeftIconSizeProperty, value);
    }

    // RightIconSize DependencyProperty
    public static readonly DependencyProperty RightIconSizeProperty = DependencyProperty.Register(
        nameof(RightIconSize), typeof(double), typeof(SearchBoxControl), new PropertyMetadata(27.0));

    public double RightIconSize
    {
        get => (double)GetValue(RightIconSizeProperty);
        set => SetValue(RightIconSizeProperty, value);
    }

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

    // IsServiceRunning DependencyProperty
    public static readonly DependencyProperty IsServiceRunningProperty = DependencyProperty.Register(
        nameof(IsServiceRunning), typeof(bool), typeof(SearchBoxControl),
        new PropertyMetadata(true));

    public bool IsServiceRunning
    {
        get => (bool)GetValue(IsServiceRunningProperty);
        set => SetValue(IsServiceRunningProperty, value);
    }
}
