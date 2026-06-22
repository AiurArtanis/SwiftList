using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using SwiftList.Core;
using SwiftList.App.Services;
using SwiftList.App.Views.QuickSearchWindow;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using SwiftList.App.ViewModels.Search;
namespace SwiftList.App;

public partial class QuickSearchWindow : Window, ISearchWindow
{
    private readonly QuickSearchViewModel _viewModel;
    private TrayIconService? _trayService;
    private ShellMenuPresenter? _menuPresenter;
    private bool _isFirstLoad = true;
    private readonly QuickSearchWindowController _controller;
    private readonly QuickSearchWindowInputHandler _inputHandler;
    private readonly QuickSearchWindowLayoutManager _layoutManager;

    public QuickSearchWindow()
    {
        InitializeComponent();
        _viewModel = new QuickSearchViewModel();
        this.DataContext = _viewModel;
        _controller = new QuickSearchWindowController(this);
        _inputHandler = new QuickSearchWindowInputHandler(this);
        _layoutManager = new QuickSearchWindowLayoutManager(this);
        InitializeChildControls();
    }

    // ==========================================

    // Decoupled Property Exposures

    // ==========================================

    public ShellMenuPresenter? MenuPresenter => _menuPresenter;
    public QuickSearchViewModel ViewModel => _viewModel;
    public string SearchText => TxtSearch.Text;

    // ==========================================

    // Child Control Properties

    // ==========================================

    public TextBox TxtSearch => SearchBox.SearchTextBox;
    public TextBlock TxtPlaceholder => SearchBox.PlaceholderTextBlock;
    public UIElement ResultsPanel => ResultsPanelControl;
    public Border GridLoading => ResultsPanelControl.LoadingBorder;
    public System.Windows.Controls.Control ProgressLoading => ResultsPanelControl.LoadingProgressBar;
    public TextBlock TxtLoadingTitle => ResultsPanelControl.LoadingTitleTextBlock;
    public TextBlock TxtLoadingStats => ResultsPanelControl.LoadingStatsTextBlock;
    public Button BtnInstallService => ResultsPanelControl.InstallServiceButton;
    public ListBox LstResults => ResultsPanelControl.ResultsListBox;
    public Grid GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    public Grid GridActions => ResultsPanelControl.ActionsGrid;
    public TextBlock TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    public ListBox LstActions => ResultsPanelControl.ActionsListBox;
    public void UpdateActionsLayout() => _layoutManager.UpdateActionsLayout();

    public void FocusSearch()
    {
        TxtSearch.Focus();
        Keyboard.Focus(TxtSearch);
    }
    public UIElement StatusBar => StatusBarControl;
    public System.Windows.Shapes.Ellipse DotStatus => StatusBarControl.StatusDot;
    public TextBlock TxtStatusInfo => StatusBarControl.StatusInfoTextBlock;

    private void InitializeChildControls()
    {
        _menuPresenter = new ShellMenuPresenter(this);
        _trayService = new TrayIconService(_viewModel, ShowWindow, ToggleVisibility);

        // Wire up event handlers to subcontrols

        BtnOpenMore.Click += BtnOpenMore_Click;
        LstResults.PreviewMouseLeftButtonUp += LstResults_PreviewMouseLeftButtonUp;
        LstResults.PreviewMouseRightButtonUp += LstResults_PreviewMouseRightButtonUp;
        LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;

        _viewModel.Results.CollectionChanged += (s, e) => _layoutManager.QueueResultsLayoutUpdate();

        LstResults.SelectionChanged += (s, e) =>
        {
            if (LstResults.SelectedItem is AppSearchResult result && !result.IsSearchSectionHeader && !result.IsEmptyResult && !result.IsApplication && result.FullPath != "__SHOW_MORE__")
            {
                QuickLookManager.Instance.UpdateOrShow(this, result.FullPath);
            }
            else
            {
                QuickLookManager.Instance.Hide();
            }
        };
    }

    private void QueueResultsLayoutUpdate() => _layoutManager.QueueResultsLayoutUpdate();

    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e) => _layoutManager.UpdateShortcutHints();

    public void UpdateShortcutHints() => _layoutManager.UpdateShortcutHints();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Log("[QuickSearchWindow] Window loaded. Registering hotkey and triggering index build.", LogLevel.Debug);

        // Hide from Alt+Tab by setting WS_EX_TOOLWINDOW
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GWL_EXSTYLE);
            var newExStyle = new IntPtr(exStyle.ToInt64() | Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
            Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GWL_EXSTYLE, newExStyle);
        }

        if (ThemeManager.Instance.ActiveTheme != null)
        {
            Helpers.WindowEffectHelper.ApplyThemeEffects(this, ThemeManager.Instance.ActiveTheme);
        }

        // Start index build

        _viewModel.TriggerIndexBuild();

        // Position the window

        _controller.PositionWindow();

        // Focus search box or hide on first launch

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            this.Hide();
        }

        else
        {
            TxtSearch.Focus();
        }
    }

    // ==========================================

    // Window Actions delegation

    // ==========================================

    public void ShowWindow() => _controller.ShowWindow(null);
    public void ShowWindow(string? initialQuery) => _controller.ShowWindow(initialQuery);
    public void HideWindow() => _controller.HideWindow(true);
    public void HideWindowNoRestore() => _controller.HideWindow(false);
    public void ToggleVisibility() => _controller.ToggleVisibility();
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path, TxtSearch.Text, HideWindow);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path, TxtSearch.Text, HideWindow);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public static T? FindVisualParentExternal<T>(DependencyObject? child) where T : DependencyObject => FindVisualParent<T>(child);

    private void Window_Deactivated(object sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
                                                                        {
                                                                            if (!IsActive)
                                                                            {
                                                                                _controller.HideWindow();
                                                                            }

                                                                        }), DispatcherPriority.Background);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);

    private void BtnOpenMore_Click(object sender, RoutedEventArgs e) => FileExecutor.OpenFileOrFolder("__SHOW_MORE__", TxtSearch.Text, HideWindowNoRestore);

    private void LstResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var asAdmin = Keyboard.Modifiers == Helpers.WpfUiHelper.GetWpfModifier(UserSettings.Load().SelectIndexModifier);
            ExecuteSearchResult(result, asAdmin);
        }
    }

    private void LstResults_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            LstResults.SelectedItem = result;
            _menuPresenter?.EnterActionsMode(result);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            if (child is FrameworkContentElement fce)
            {
                child = fce.Parent;
            }

            else
            {
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
        }

        return null;
    }

    private void ExecuteSearchResult(AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsSearchSectionHeader)
            return;
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(result.FullPath);
        }

        if (result.IsPluginSearchAction)
        {
            HideWindow();
            if (PluginManager.Instance.TryExecuteSearchAction(result, this))
            {
            }

            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, this))
        {
            HideWindow();
            return;
        }

        var currentQuery = TxtSearch.Text;
        if (result.FullPath == "__SHOW_MORE__")
        {
            HideWindowNoRestore();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, HideWindowNoRestore);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, HideWindowNoRestore);
        }

        else
        {
            HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, HideWindow);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, HideWindow);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        _trayService?.Dispose();
        _menuPresenter?.Dispose();
        base.OnClosed(e);
    }
}
