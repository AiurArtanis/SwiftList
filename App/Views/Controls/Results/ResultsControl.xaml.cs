using System.Windows;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Specialized;

namespace SwiftList.App.Views.Controls.Results;

public enum ResultsViewMode
{
    List,
    Grid
}

public partial class ResultsControl : System.Windows.Controls.UserControl
{
    public ResultsControl()
    {
        InitializeComponent();
        InitializeSelectionChangedHandlers();
        ResultsDragDropHelper.Register(LstResults);
        ResultsDragDropHelper.Register(LstGridResults);

        // List mode only (quick/inline windows): hovering a row selects it, matching how Spotlight/
        // Alfred-style launchers behave. Rows with IsHitTestVisible="False" (section headers, the
        // empty-result placeholder -- see ResultItemStyle) never resolve to a ListBoxItem here, so
        // they're naturally skipped without any extra checks.
        //
        // WPF re-hit-tests a stationary mouse whenever the visual tree changes underneath it (rows
        // relaid out as results repopulate), and synthesizes a MouseMove for that even though the
        // cursor never physically moved. If it happens to now sit over row 2+ (e.g. results expanded
        // under a cursor that was resting there from a previous window position), that synthetic event
        // used to steal selection away from the row 0 default OnCollectionChanged just set. Only treat
        // a MouseMove as real hover if the coordinate actually changed since the last one seen --
        // _lastHoverPos is reseeded to wherever the cursor currently sits every time OnCollectionChanged
        // resets selection, so the first (synthetic) move after a refresh always matches and is ignored.
        LstResults.MouseMove += (s, e) =>
        {
            var pos = e.GetPosition(LstResults);
            if (_lastHoverPos.HasValue && pos == _lastHoverPos.Value) return;
            _lastHoverPos = pos;

            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.Content != null && !ReferenceEquals(LstResults.SelectedItem, item.Content))
            {
                LstResults.SelectedItem = item.Content;
            }
        };

        // Dynamically load custom GridView columns from ResultColumnProviders
        Loaded += (s, e) =>
        {
            UpdateViewModeVisibility();
            LoadDynamicColumns();
        };

        // Every grid column's header (built-in and plugin alike) can end up as a one-time translated
        // snapshot rather than a live binding -- plugin columns always are (PopulateDynamicColumns sets
        // Header as a literal string), and a built-in column's own live XAML binding is overwritten the
        // first time it's clicked/sorted (see ResultsControlColumns' own comment). Re-resolve them all on
        // every TranslationManager change so none stay stuck in whatever language was active at that
        // point; harmless no-op for List-mode-only owners (QuickSearchWindow/InlineSearchWindow) since
        // LstGridResults still exists there, just hidden. Unsubscribes on Unloaded so a closed
        // SearchWindow's ResultsControl doesn't linger forever pinned by the singleton's event -- never
        // fires for QuickSearchWindow/InlineSearchWindow's instances since those windows are only ever
        // Hidden, not Closed, which is exactly the lifetime this subscription should have there too.
        Services.TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
        Unloaded += (s, e) => Services.TranslationManager.Instance.PropertyChanged -= OnTranslationsChanged;
    }

    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
            ResultsControlColumns.RefreshAllColumnHeaders(LstGridResults);
    }

    private System.Windows.Point? _lastHoverPos;

    internal static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = child is FrameworkContentElement fce ? fce.Parent : System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public Border LoadingBorder => null!;
    public System.Windows.Controls.Control LoadingProgressBar => null!;
    public TextBlock LoadingTitleTextBlock => null!;
    public TextBlock LoadingStatsTextBlock => null!;
    public System.Windows.Controls.Button InstallServiceButton => null!;
    public System.Windows.Controls.ListBox ResultsListBox => LstResults;
    public Grid SearchResultsGrid => GridSearchResultsContainer;
    public Grid ActionsGrid => GridActions;
    public TextBlock ActionsTargetTextBlock => TxtActionsTarget;
    public System.Windows.Controls.ListBox ActionsListBox => LstActions;

    public System.Windows.Controls.ListBox ActiveListBox => ViewMode == ResultsViewMode.Grid ? (System.Windows.Controls.ListBox)LstGridResults : LstResults;

    // ViewMode DependencyProperty
    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode), typeof(ResultsViewMode), typeof(ResultsControl),
        new PropertyMetadata(ResultsViewMode.List, OnViewModeChanged));

    public ResultsViewMode ViewMode
    {
        get => (ResultsViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    private static void OnViewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateViewModeVisibility();
        }
    }

    private void UpdateViewModeVisibility()
    {
        if (GridSearchResults == null || GridSearchResultsGrid == null) return;
        if (ViewMode == ResultsViewMode.Grid)
        {
            GridSearchResults.Visibility = Visibility.Collapsed;
            GridSearchResultsGrid.Visibility = Visibility.Visible;
        }
        else
        {
            GridSearchResults.Visibility = Visibility.Visible;
            GridSearchResultsGrid.Visibility = Visibility.Collapsed;
        }
    }

    // ItemsSource DependencyProperty
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ResultsControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateItemsSource(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
        }
    }

    private void UpdateItemsSource(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldNotify)
        {
            oldNotify.CollectionChanged -= OnCollectionChanged;
        }

        LstResults?.ItemsSource = newValue;
        LstGridResults?.ItemsSource = newValue;

        if (newValue is INotifyCollectionChanged newNotify)
        {
            newNotify.CollectionChanged += OnCollectionChanged;
        }
    }

    // ReconcileTo (ObservableRangeCollection) raises one CollectionChanged event PER CHANGED ROW, not
    // one for the whole batch -- and SearchResultsReconciler.ItemsEqual compares SearchQuery, which is
    // re-stamped with the just-typed text on every keystroke, so essentially every row differs every
    // time (needed so an already-realized row's TextHighlighter binding picks up the new query and
    // re-highlights). For the full window's 1000-item budget that's up to ~1000 individual events per
    // keystroke; without this guard, each one independently scheduled its own Dispatcher.BeginInvoke
    // below, so a single keystroke could queue up to ~1000 Render-priority callbacks (each redoing
    // SelectedIndex/ScrollIntoView) for the UI thread to drain before it could respond to the next one --
    // only the LAST of those ever did anything observable anyway, since the list had already fully
    // settled to its final state by the time any of them actually ran. Collapsing the whole burst down to
    // exactly one scheduled callback (it naturally runs after ReconcileTo's synchronous loop finishes,
    // since BeginInvoke never runs mid-loop on the same thread) keeps the exact same observable result
    // with none of the wasted intermediate work.
    private bool _collectionChangedPending;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_collectionChangedPending) return;
        _collectionChangedPending = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _collectionChangedPending = false;

            if (GridActions != null && GridActions.Visibility == Visibility.Visible)
                return;

            var list = ActiveListBox;
            if (list != null && list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
                if (ViewMode == ResultsViewMode.Grid)
                    LstGridResults.ScrollIntoView(LstGridResults.SelectedItem);
                else
                    LstResults.ScrollIntoView(LstResults.SelectedItem);
            }
            else
            {
                list?.SelectedIndex = -1;
            }

            // Reseed the hover baseline to the cursor's current spot so the MouseMove WPF synthesizes
            // once these rows finish laying out under it doesn't get mistaken for real movement (see
            // LstResults.MouseMove above).
            _lastHoverPos = System.Windows.Input.Mouse.GetPosition(LstResults);
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    // SelectedItem DependencyProperty
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ResultsControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public object SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateSelectedItem(e.NewValue);
        }
    }

    private bool _isUpdatingSelection;

    private void UpdateSelectedItem(object value)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        try
        {
            if (ViewMode == ResultsViewMode.Grid)
            {
                LstGridResults?.SelectedItem = value;
            }
            else
            {
                LstResults?.SelectedItem = value;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void InitializeSelectionChangedHandlers()
    {
        LstResults.SelectionChanged += (s, e) =>
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            try
            {
                SelectedItem = LstResults.SelectedItem;
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        };

        LstGridResults.SelectionChanged += (s, e) =>
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            try
            {
                SelectedItem = LstGridResults.SelectedItem;
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        };
    }

    private bool _columnsLoaded;
    private void LoadDynamicColumns()
    {
        if (_columnsLoaded || LstGridResults == null) return;
        _columnsLoaded = true;
        ResultsControlColumns.PopulateDynamicColumns(LstGridResults);

        // Grid mode only (the full window) -- Quick/Inline windows' DataContext has no
        // CurrentSortColumn/IsSortAscending pair to read, and never show LstGridResults anyway.
        if (ViewMode == ResultsViewMode.Grid)
        {
            ResultsControlColumns.ApplyColumnOrder(LstGridResults, Core.UserSettings.Load().ColumnOrder);
            ResultsControlColumns.ApplyInitialSortIndicator(LstGridResults, DataContext);
        }
    }

    // sender is always the ListView itself (that's where GridViewColumnHeader.Click="..." attaches the
    // handler in XAML) -- WPF only walks the handler UP the tree via routing, it doesn't rewrite
    // `sender` to whatever was actually clicked. The real clicked element is e.OriginalSource, which for
    // a click on the header's own text/content is some element INSIDE the header (a ContentPresenter,
    // a TextBlock, ...), so it needs walking back up to find the enclosing GridViewColumnHeader.
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) =>
        ResultsControlColumns.HandleColumnHeaderClick(
            FindVisualParent<GridViewColumnHeader>(e.OriginalSource as DependencyObject), DataContext, LstGridResults);
}
