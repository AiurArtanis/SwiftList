using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SwiftList.App;

public partial class SearchBoxControl : UserControl
{
    // Raised when either the left- or right-docked status icon is right-clicked. Purely a passthrough --
    // this control doesn't know what "reset" means for whichever window hosts it (only the quick popup
    // window currently wires this up, to reset its own saved position).
    public event Action? IconRightClicked;

    // Raised when the icon is left-clicked, with the click's screen coordinates (physical pixels, same
    // convention QuickNavigationMenu.Show expects) for callers that need to anchor a popup there. Only
    // meaningful when IsIconClickable is set -- see that property's own comment.
    public event Action<int, int>? IconLeftClicked;

    private void Icon_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => IconRightClicked?.Invoke();

    // Marks the press handled so it never bubbles up to a hosting window's own MouseLeftButtonDown
    // (e.g. the quick window's Border_MouseLeftButtonDown, which calls DragMove()): DragMove captures
    // the mouse for the rest of the gesture, which swallows the matching MouseLeftButtonUp below before
    // it ever reaches this control -- so without this, a plain click on a clickable icon just silently
    // starts (and instantly ends) a drag instead of registering as a click. Left alone when the icon
    // isn't clickable, so windows that never opted in keep whatever click-to-drag behavior they had.
    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsIconClickable) e.Handled = true;
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsIconClickable || IconLeftClicked == null) return;
        var screenPoint = ((System.Windows.Media.Visual)sender).PointToScreen(e.GetPosition((IInputElement)sender));
        IconLeftClicked.Invoke((int)screenPoint.X, (int)screenPoint.Y);
    }

    static SearchBoxControl()
    {
        PaddingProperty.OverrideMetadata(typeof(SearchBoxControl),
            new FrameworkPropertyMetadata(new Thickness(21, 4, 21, 4)));
        FontSizeProperty.OverrideMetadata(typeof(SearchBoxControl),
            new FrameworkPropertyMetadata(35.0));
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
        // FontSize = height * 0.4 (e.g. 70px -> 28px, 50px -> 20px). Text/cursor previously filled
        // only ~41% of the box height (0.34 coefficient), leaving a visibly oversized gap above and
        // below the single line of text -- raising the ratio (and shrinking vertical padding below to
        // match, so the bigger text still fits) is what actually closes that gap; padding alone can't,
        // since content is vertically centered and just absorbs whatever padding leaves behind.
        FontSize = Math.Clamp(height * 0.4, 12.0, 36.0);

        // Vertical Padding:
        // Keep horizontal padding fixed at 21, scale vertical padding dynamically (e.g. 70px -> 7px, 50px -> 5px)
        var verticalPadding = Math.Clamp(height * 0.1, 4.0, 20.0);
        Padding = new Thickness(21, verticalPadding, 21, verticalPadding);

        // Scale Left and Right icon sizes (e.g. Left: 70px -> 18.2px, Right: 70px -> 39.9px). Right kept
        // at the same size-to-FontSize ratio as before (~1.15x) so it grows in step with the bigger text
        // from the FontSize coefficient bump above, instead of looking undersized next to it.
        LeftIconSize = Math.Clamp(height * 0.26, 10.0, 30.0);
        RightIconSize = Math.Clamp(height * 0.57, 15.0, 45.0);
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

    // IsIconClickable DependencyProperty: false by default, so windows that never wire up
    // IconLeftClicked (the main search window, and the inline window when it isn't docked to a file
    // picker) get no hover highlight, no hand cursor, and no tooltip on an icon that would otherwise
    // silently do nothing if clicked.
    public static readonly DependencyProperty IsIconClickableProperty = DependencyProperty.Register(
        nameof(IsIconClickable), typeof(bool), typeof(SearchBoxControl), new PropertyMetadata(false));

    public bool IsIconClickable
    {
        get => (bool)GetValue(IsIconClickableProperty);
        set => SetValue(IsIconClickableProperty, value);
    }

    // IconClickHint DependencyProperty: tooltip text shown on hover, only when IsIconClickable is set.
    public static readonly DependencyProperty IconClickHintProperty = DependencyProperty.Register(
        nameof(IconClickHint), typeof(string), typeof(SearchBoxControl), new PropertyMetadata(null));

    public string? IconClickHint
    {
        get => (string?)GetValue(IconClickHintProperty);
        set => SetValue(IconClickHintProperty, value);
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
