using System.Windows;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Specialized;
using SwiftList.App.Services;

namespace SwiftList.App;

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
        Views.Controls.ResultsDragDropHelper.Register(LstResults);
        Views.Controls.ResultsDragDropHelper.Register(LstGridResults);

        // List mode only (quick/inline windows): hovering a row selects it, matching how Spotlight/
        // Alfred-style launchers behave. Rows with IsHitTestVisible="False" (section headers, the
        // empty-result placeholder -- see ResultItemStyle) never resolve to a ListBoxItem here, so
        // they're naturally skipped without any extra checks.
        LstResults.MouseMove += (s, e) =>
        {
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
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
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

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
                                                                                                 {
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
                                                                                                 }), System.Windows.Threading.DispatcherPriority.Render);

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
        Views.Controls.ResultsControlColumns.PopulateDynamicColumns(LstGridResults);
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) =>
        Views.Controls.ResultsControlColumns.HandleColumnHeaderClick(sender, DataContext, LstGridResults);
}
