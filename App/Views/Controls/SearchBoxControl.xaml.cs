using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;

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

    // Middle-clicking the logo toggles Stay Open (#197), as a second way in alongside the hotkey.
    public event Action? IconMiddleClicked;

    private void Icon_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => IconRightClicked?.Invoke();

    // WPF has no dedicated middle-button event, so filter the general MouseUp event.
    private void Icon_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        IconMiddleClicked?.Invoke();
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
        DataObject.AddPastingHandler(TxtSearch, OnSearchTextPasting);
    }

    // TxtSearch is a single-line TextBox (AcceptsReturn defaults to false), whose default paste command
    // silently truncates multi-line clipboard content down to just its first line -- pasting several
    // filenames copied one-per-line from a spreadsheet or text file used to leave only the first one in
    // the box. Everything has a similar convenience feature (wrapping a multi-line paste into an OR
    // query), and SwiftList already has an equivalent OR operator of its own (see search-syntax.md's
    // `report | summary`), so this just needs to fold the pasted lines into that syntax instead of letting
    // the default paste drop them. Left untouched for a normal single-line paste (the common case) so
    // existing behavior there is unaffected.
    // Cancels the default paste and inserts the transformed text directly through the TextBox's own
    // SelectedText API instead of trying to hand WPF's internal paste command a substitute DataObject/
    // FormatToApply to consume -- that approach silently pasted nothing at all in real use (verified by
    // hand) despite looking correct in isolated unit tests, and there's no reliable way to prove from here
    // whether the internal command actually honors a replaced DataObject at all. Doing the insertion
    // ourselves sidesteps that uncertainty entirely: there's no internal pipeline left to second-guess.
    internal static void OnSearchTextPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) && !e.DataObject.GetDataPresent(DataFormats.Text))
            return;

        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length < 2)
            return; // Not a multi-line paste -- let the default single-line paste behavior run as-is.

        var joined = string.Join(" | ", lines);
        var insertAt = textBox.SelectionStart;
        textBox.SelectedText = joined; // Replaces the current selection, same as a real paste would.
        textBox.SelectionStart = insertAt + joined.Length;
        textBox.SelectionLength = 0;
        e.CancelCommand();
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

    // IsIconDraggable DependencyProperty: only the quick window sets this -- its logo already moves the
    // window on a plain drag, and this lets that keep working alongside IconLeftClicked (a real drag vs.
    // a click that opens a menu are told apart by movement distance; see Icon_MouseMove). Windows with no
    // drag behavior on their icon (the inline window) leave this false, so a press-release always reads
    // as a click with no distance-threshold wait.
    public static readonly DependencyProperty IsIconDraggableProperty = DependencyProperty.Register(
        nameof(IsIconDraggable), typeof(bool), typeof(SearchBoxControl), new PropertyMetadata(false));

    public bool IsIconDraggable
    {
        get => (bool)GetValue(IsIconDraggableProperty);
        set => SetValue(IsIconDraggableProperty, value);
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

    // Shown on the logo while the quick window has been asked not to auto-hide.
    public static readonly DependencyProperty IsStayOpenProperty = DependencyProperty.Register(
        nameof(IsStayOpen), typeof(bool), typeof(SearchBoxControl), new PropertyMetadata(false));

    public bool IsStayOpen
    {
        get => (bool)GetValue(IsStayOpenProperty);
        set => SetValue(IsStayOpenProperty, value);
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
