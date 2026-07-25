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
using SwiftList.App.Views.QuickSearchWindow.Helpers;
using SwiftList.App.Services.AppWindow;

using SwiftList.App.Services.Tray;

using SwiftList.App.Services.Theme;
using SwiftList.App.Services.ShellMenu.Presenter;
using SwiftList.App.Helpers.Visuals;
namespace SwiftList.App;

public partial class QuickSearchWindow : Window, ISearchWindow, IHasVisibleContentInset
{
    private readonly QuickSearchViewModel _viewModel;
    private TrayIconService? _trayService;
    private ShellMenuPresenter? _menuPresenter;
    private bool _isFirstLoad = true;
    private readonly QuickSearchWindowController _controller;
    private readonly QuickSearchWindowInputHandler _inputHandler;
    private readonly QuickSearchWindowLayoutManager _layoutManager;
    private readonly QuickSearchWindowResultExecutor _resultExecutor;
    internal QuickSearchKeywordHistoryController KeywordHistoryController { get; private set; } = null!;

    // Must match QuickSearchWindow.xaml's root Border Margin ("24,40,24,24").
    public Thickness VisibleContentInset => new(24, 40, 24, 24);

    public QuickSearchWindow()
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        SystemMenuBlocker.Attach(this);
        _viewModel = new QuickSearchViewModel();
        this.DataContext = _viewModel;
        _controller = new QuickSearchWindowController(this);
        _inputHandler = new QuickSearchWindowInputHandler(this);
        _layoutManager = new QuickSearchWindowLayoutManager(this);
        _resultExecutor = new QuickSearchWindowResultExecutor(this);
        InitializeChildControls();
    }

    // ==========================================

    // Decoupled Property Exposures

    // ==========================================

    public ShellMenuPresenter? MenuPresenter => _menuPresenter;
    public QuickSearchViewModel ViewModel => _viewModel;
    public string SearchText => TxtSearch.Text;

    public bool IsInActionsMode
    {
        get => SearchBox.IsInActionsMode;
        set
        {
            SearchBox.IsInActionsMode = value;
            _viewModel.Search.IsActionsMode = value;
        }
    }

    // ==========================================

    // Child Control Properties

    // ==========================================

    public TextBox TxtSearch => SearchBox.SearchTextBox;
    public TextBox SearchTextBox => SearchBox.SearchTextBox;
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

        SearchBox.IconRightClicked += _controller.ResetPosition;
        // IsIconDraggable keeps the logo's existing "drag moves the window" behavior working alongside
        // IconLeftClicked: SearchBoxControl tells a real drag apart from a plain click by movement
        // distance (see its own Icon_MouseMove), so this needs BOTH flags rather than picking one.
        SearchBox.IconLeftClicked += (_, _) => ShowTrayMenu();
        SearchBox.IconDragCompleted += SaveWindowPosition;
        SearchBox.IsIconClickable = true;
        SearchBox.IsIconDraggable = true;
        SearchBox.IconClickHint = TranslationManager.Instance["QuickSearch_LogoDragResetHint"];
        LstResults.PreviewMouseLeftButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseLeftButtonUp(e);
        LstResults.PreviewMouseRightButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseRightButtonUp(e);
        LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;

        _viewModel.Results.CollectionChanged += (s, e) => _layoutManager.QueueResultsLayoutUpdate();

        KeywordHistoryController = new QuickSearchKeywordHistoryController(this);

        LstResults.SelectionChanged += (s, e) =>
        {
            if (LstResults.SelectedItem is AppSearchResult result && result.CanPreview)
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

    private static readonly int WM_TASKBARCREATED = Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.RegisterWindowMessage("TaskbarCreated");

    // Fires as soon as the Win32 HWND exists, before WPF's first layout/render pass -- the earliest
    // point CompositionTarget.RenderMode can be set. This window is created once at startup and only
    // ever Hidden, never Closed -- its DirectX composition surface stays alive for the whole process
    // lifetime, which NVIDIA Advanced Optimus treats as "GPU in use" and refuses to hot-switch around
    // (GitHub #82). Forcing software rendering on just this one window's HwndTarget avoids that,
    // without touching SettingsWindow/SearchWindow/etc. (which are properly closed and don't trigger
    // this). Setting it here rather than in Window_Loaded (which runs after the window has already
    // been shown once) matters: by Loaded, WPF may already have created the hardware D3D device for
    // this HwndTarget, and RenderMode only governs FUTURE frames, not retroactively releasing one
    // that's already backing this window. Opt-out setting since it costs this window its hardware
    // acceleration.
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (!UserSettings.Load().EnableHardwareAcceleration
            && PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource hwndSource
            && hwndSource.CompositionTarget is System.Windows.Interop.HwndTarget hwndTarget)
        {
            hwndTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }
    }

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

        // Re-add the tray icon if explorer.exe restarts mid-session (crash, shell update) -- Windows
        // silently drops every previously-registered tray icon and broadcasts this message so still-
        // running apps know to re-add theirs.
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource hwndSource)
        {
            hwndSource.AddHook((IntPtr wnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_TASKBARCREATED)
                    _trayService?.HandleTaskbarCreated();
                return IntPtr.Zero;
            });
        }

        if (ThemeManager.Instance.ActiveTheme != null)
        {
            WindowEffectHelper.ApplyThemeEffects(this, ThemeManager.Instance.ActiveTheme);
        }

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

    // Called by GeneralSettingsViewModel.Apply() when the "hide tray icon" setting changes, so the
    // real NotifyIcon updates live without needing a restart. The search box logo's own menu (see
    // ShowTrayMenu) is unaffected by this setting -- it's a permanent, always-visible entry point
    // regardless of the tray icon's state.
    public void ApplyTrayIconVisibility(bool hideTrayIcon) => _trayService?.SetTrayIconVisible(!hideTrayIcon);

    public void ShowWindow() => _controller.ShowWindow(null);
    public void ShowWindow(string? initialQuery) => _controller.ShowWindow(initialQuery);
    public void HideWindow() => _controller.HideWindow(true);
    public void HideWindowNoRestore() => _controller.HideWindow(false);
    public void SuppressNextForegroundRestore() => _controller.SuppressNextRestore();
    public void ToggleVisibility() => _controller.ToggleVisibility();
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path, TxtSearch.Text, HideWindow);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path, TxtSearch.Text, HideWindow);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public static T? FindVisualParentExternal<T>(DependencyObject? child) where T : DependencyObject => FindVisualParent<T>(child);

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // A transient foreground steal can deactivate us mid-typing -- e.g. reading a \\wsl$ result's icon
        // or modified date on a background thread wakes the WSL VM, whose cold start briefly flashes a
        // conhost that grabs the foreground. Wait a moment and only hide if still inactive, so focus that
        // bounces right back doesn't drop the search window.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (s, _) =>
        {
            timer.Stop();
            if (IsActive) return;
            // QuickLookManager just Hide()'d this window itself, for a preview handler's own popup
            // dialog (see its own comment) -- not a real deactivation to react to.
            if (Services.QuickLookManager.Instance.IsHiddenForDialog) return;
            // Do not hide if there are visible owned windows (e.g. a crash MessageBox dialog).
            foreach (Window owned in OwnedWindows)
                if (owned.IsVisible) return;

            // If activation moved to one of our own other windows (e.g. Settings/About opened via the
            // search box logo's own menu), restoring foreground to whatever was active before this
            // window was shown would immediately steal it right back off that window. Only restore focus
            // when the user has actually left the app entirely.
            var movedToOwnWindow = false;
            foreach (Window w in System.Windows.Application.Current.Windows)
                if (w != this && w.IsActive) { movedToOwnWindow = true; break; }

            _controller.HideWindow(restoreFocus: !movedToOwnWindow);
        };
        timer.Start();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            this.DragMove();
            SaveWindowPosition();
        }
    }

    private void SaveWindowPosition()
    {
        var settings = UserSettings.Load();
        settings.SearchWindow.Left = this.Left;
        settings.SearchWindow.Top = this.Top;
        settings.Save();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);

    // The search box logo's own left-click (see SearchBox.IconLeftClicked wiring in the constructor)
    // opens the same menu the tray icon's right-click shows, anchored at the cursor rather than the tray
    // icon's dummy-window+mouse-point placement. "Show Main Window" from here additionally carries over
    // whatever query is currently typed.
    private void ShowTrayMenu()
    {
        var queryText = (IsInActionsMode && _menuPresenter != null) ? _menuPresenter.SavedSearchQuery : TxtSearch.Text;
        _trayService?.ShowMenuAt(RootGrid, () => FileExecutor.OpenFileOrFolder("__SHOW_MORE__", SearchResultTypePriority.StripLeadingTrigger(queryText), HideWindowNoRestore));
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

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        _trayService?.Dispose();
        _menuPresenter?.Dispose();
        base.OnClosed(e);
    }
}
