using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.Views.SearchWindow;
using SwiftList.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using ListView = System.Windows.Controls.ListView;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using SwiftList.App.ViewModels.Search;
namespace SwiftList.App;

public partial class SearchWindow : Window, ISearchWindow, IHasVisibleContentInset
{
    private readonly SearchViewModel _viewModel;
    private readonly SearchWindowChromeHandler _chromeHandler;
    private readonly SearchWindowInputHandler _inputHandler;
    private readonly ShellMenuPresenter _menuPresenter;

    // Must match SearchWindow.xaml's MainBorder Margin.
    public Thickness VisibleContentInset => new(8);

    public SearchWindow(string initialQuery = "")
    {
        InitializeComponent();
        _menuPresenter = new ShellMenuPresenter(this);
        _chromeHandler = new SearchWindowChromeHandler(this);
        _inputHandler = new SearchWindowInputHandler(this);
        this.PreviewKeyDown += Window_PreviewKeyDown;
        this.StateChanged += SearchWindow_StateChanged;

        // The maximized-size cap that keeps the window off the taskbar is applied per-monitor in
        // SearchWindowChromeHandler.HandleStateChanged, so it stays correct on secondary screens.

        _viewModel = new SearchViewModel(initialQuery);
        this.DataContext = _viewModel;

        QuickLookManager.Instance.Reset();

        this.Loaded += (s, e) =>
        {
            this.Activate();
            this.Focus();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var txtSearch = SearchBox.SearchTextBox;
                txtSearch.Focus();
                Keyboard.Focus(txtSearch);
                if (initialQuery != null)
                {
                    txtSearch.SelectionStart = initialQuery.Length;
                }

            }), System.Windows.Threading.DispatcherPriority.Input);
        };

        // Bind list events
        var activeList = ResultsPanelControl.ActiveListBox;
        activeList.KeyDown += LstGridResults_KeyDown;
        activeList.MouseDoubleClick += LstGridResults_MouseDoubleClick;
        activeList.PreviewMouseRightButtonUp += LstGridResults_PreviewMouseRightButtonUp;
        activeList.PreviewMouseWheel += LstGridResults_PreviewMouseWheel;
        activeList.SelectionChanged += (s, e) =>
        {
            if (activeList.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                QuickLookManager.Instance.UpdateOrShow(this, result.FullPath);
            }
            else
            {
                QuickLookManager.Instance.Hide();
            }
        };

        ResultsPanelControl.ActionsListBox.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;
    }

    // ==========================================

    // ISearchWindow Interface Implementation

    // ==========================================

    public UIElement ResultsPanel => ResultsPanelControl;
    ListBox ISearchWindow.LstResults => ResultsPanelControl.ActiveListBox;
    Grid ISearchWindow.GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    Grid ISearchWindow.GridActions => ResultsPanelControl.ActionsGrid;
    TextBlock ISearchWindow.TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    ListBox ISearchWindow.LstActions => ResultsPanelControl.ActionsListBox;
    public string SearchText => SearchBox.SearchTextBox.Text;
    public TextBox SearchTextBox => SearchBox.SearchTextBox;

    public bool IsInActionsMode
    {
        get => SearchBox.IsInActionsMode;
        set
        {
            SearchBox.IsInActionsMode = value;
            _viewModel?.IsActionsMode = value;
        }
    }

    public void UpdateActionsLayout() { /* Fixed-size window, no dynamic resizing needed */ }

    public void FocusSearch()
    {
        SearchBox.SearchTextBox.Focus();
        Keyboard.Focus(SearchBox.SearchTextBox);
    }

    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void HideWindow() => this.Close();

    // ==========================================

    // Window Control Exposures for Handlers

    // ==========================================

    public TextBox TxtSearchBoxControl => SearchBox.SearchTextBox;
    public ListView LstGridResultsControl => (ListView)ResultsPanelControl.ActiveListBox;
    public ShellMenuPresenter MenuPresenter => _menuPresenter;

    // ==========================================

    // Window Chrome & Drag Handlers

    // ==========================================

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _chromeHandler.HandleHeaderMouseLeftButtonDown(sender, e);
    private void SearchWindow_StateChanged(object? sender, EventArgs e) => _chromeHandler.HandleStateChanged();
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => _chromeHandler.Minimize();
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => _chromeHandler.ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => _chromeHandler.Close();

    private void BtnBackToQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        var quickSearchWindow = System.Windows.Application.Current.MainWindow as QuickSearchWindow;
        if (quickSearchWindow == null)
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is QuickSearchWindow qsw)
                {
                    quickSearchWindow = qsw;
                    break;
                }
            }
        }

        if (quickSearchWindow != null)
        {
            string? query = null;
            if (!string.IsNullOrWhiteSpace(SearchBox.SearchText))
            {
                query = SearchBox.SearchText;
            }

            else
            {
                query = quickSearchWindow.ViewModel.SearchQuery;
            }

            quickSearchWindow.ShowWindow(query);
        }

        this.Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);

    // ==========================================

    // Results Navigation & Context Menu

    // ==========================================

    private void TxtSearchBox_KeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleTxtSearchBoxKeyDown(e);
    private void LstGridResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _inputHandler.HandleLstGridResultsMouseDoubleClick(e);
    private void LstGridResults_KeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleLstGridResultsKeyDown(e);
    private void LstGridResults_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => _inputHandler.HandleLstGridResultsPreviewMouseRightButtonUp(e);

    private void LstGridResults_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            var scrollViewer = FindScrollViewer(LstGridResultsControl);
            if (scrollViewer != null)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineLeft();
                    scrollViewer.LineLeft();
                    scrollViewer.LineLeft();
                }

                else
                {
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                }

                e.Handled = true;
            }
        }
    }

    private ScrollViewer? FindScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }

        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        // This window can be one of several (e.g. opened via "show more"), so on close release the icon
        // cache and trim the working set, matching the quick/inline windows, to reclaim its bitmaps.
        ShellIconHelper.ClearCache();
        Core.Win32Api.TrimWorkingSet();
        base.OnClosed(e);
    }
}
