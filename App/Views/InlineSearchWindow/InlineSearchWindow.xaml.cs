using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.App.ViewModels;
using ListBox = System.Windows.Controls.ListBox;
using SwiftList.App.Views.InlineSearchWindow.Helpers;
using SwiftList.App.ViewModels.Search;

namespace SwiftList.App
{
    /// <summary>
    /// Compact inline search window that appears at the bottom-right corner of
    /// the active Explorer window or Desktop when the user types any character.
    /// Results expand upward, search box stays anchored at bottom.
    /// </summary>
    public partial class InlineSearchWindow : Window, ISearchWindow
    {
        private readonly QuickSearchViewModel _viewModel;
        private readonly InlineSearchManager _manager;
        private readonly ShellMenuPresenter _menuPresenter;
        private readonly InlineSearchWindowInputHandler _inputHandler;
        private readonly InlineSearchWindowPositioner _positioner;
        private string _searchText = string.Empty;
        private IntPtr _originalLayout = IntPtr.Zero;

        public ShellMenuPresenter MenuPresenter => _menuPresenter;
        public QuickSearchViewModel ViewModel => _viewModel;
        public InlineSearchManager Manager => _manager;
        public InlineSearchWindowInputHandler InputHandler => _inputHandler;
        public InlineSearchWindowPositioner Positioner => _positioner;

        public InlineSearchWindow(QuickSearchViewModel viewModel, InlineSearchManager manager)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _manager = manager;
            this.DataContext = _viewModel;

            _menuPresenter = new ShellMenuPresenter(this);
            _inputHandler = new InlineSearchWindowInputHandler(this);
            _positioner = new InlineSearchWindowPositioner(this);

            TxtSearchDisplay.TextChanged += (s, e) =>
            {
                if (_searchText != TxtSearchDisplay.Text)
                {
                    _viewModel.IsInlineSearchContext = true;
                    _searchText = TxtSearchDisplay.Text;
                    TxtPlaceholder.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
                    _viewModel.SearchQuery = _searchText;
                }
            };

            this.PreviewKeyDown += (s, e) => _inputHandler.HandlePreviewKeyDown(e);

            // Use custom template for inline search that hides path/ParentDir
            if (TryFindResource("InlineSearchResultTemplate") is DataTemplate inlineTemplate)
            {
                LstResults.ItemTemplate = inlineTemplate;
            }

            _manager.ExplorerTracker.OnActiveWindowMoved += HandleActiveWindowMoved;

            this.IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    _positioner.PositionWindow();
                }
            };

            this.SourceInitialized += (s, e) =>
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // 1. Decouple window hierarchy: Set active Explorer/Desktop as native owner HWND
                    var tracker = _manager.ExplorerTracker;
                    if (tracker.ActiveHwnd != IntPtr.Zero)
                    {
                        InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_HWNDPARENT, tracker.ActiveHwnd);
                    }

                    // 2. Set Extended Styles: WS_EX_TOOLWINDOW (hide from Alt+Tab)
                    IntPtr exStyle = InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE);
                    exStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
                    InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE, exStyle);

                    // 3. Ensure topmost
                    InlineSearchWindowNativeMethods.SetWindowPos(hwnd, InlineSearchWindowNativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        InlineSearchWindowNativeMethods.SWP_NOMOVE | InlineSearchWindowNativeMethods.SWP_NOSIZE | InlineSearchWindowNativeMethods.SWP_SHOWWINDOW);
                }

                if (IsVisible) _positioner.PositionWindow();
            };

            this.Loaded += (s, e) =>
            {
                if (IsVisible) _positioner.PositionWindow();
            };

            this.SizeChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    _positioner.PositionWindow();
                }
            };

            _viewModel.Results.CollectionChanged += (s, e) =>
            {
                _inputHandler.SuppressExplorerSelectionSyncForResultRefresh();
                _inputHandler.QueueResultsLayoutUpdate();
            };

            // Wire scroll handler to update shortcut keys dynamically when scrolling
            LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => _inputHandler.UpdateShortcutHints()));
            LstResults.SelectionChanged += (s, e) => _inputHandler.SyncExplorerSelection();

            // Mouse actions on results list
            LstResults.MouseDoubleClick += (s, e) =>
            {
                if (LstResults.SelectedItem is AppSearchResult result)
                {
                    this.ExecuteSearchResult(result);
                }
            };

            LstResults.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var item = InlineSearchWindowInputHandler.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (item != null && item.Content is AppSearchResult result)
                {
                    if (result.FullPath == "__SHOW_MORE__")
                    {
                        e.Handled = true;
                        this.ExecuteSearchResult(result);
                    }
                }
            };

            LstResults.PreviewMouseRightButtonUp += (s, e) =>
            {
                var item = InlineSearchWindowInputHandler.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (item != null && item.Content is AppSearchResult result)
                {
                    e.Handled = true;
                    LstResults.SelectedItem = result;
                    _menuPresenter.EnterActionsMode(result);
                }
            };

            // Actions list double-click and click wiring
            LstActions.MouseDoubleClick += _menuPresenter.HandleActionsMouseDoubleClick;
            LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;

            // Trigger connection/build in view model
            _viewModel.TriggerIndexBuild();
        }

        // ==========================================
        // Exposed Child Controls matching QuickSearchWindow for ShellMenuPresenter
        // ==========================================
        public UIElement ResultsPanel => ResultsPanelControl;
        public ListBox LstResults => ResultsPanelControl.ResultsListBox;
        public Grid GridSearchResults => ResultsPanelControl.SearchResultsGrid;
        public Grid GridActions => ResultsPanelControl.ActionsGrid;
        public TextBlock TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
        public ListBox LstActions => ResultsPanelControl.LstActions;

        public string SearchText => _searchText;

        public bool ActivateAndFocusSearchBox()
        {
            IntPtr foreground = InlineSearchWindowNativeMethods.GetForegroundWindow();
            uint currentThread = InlineSearchWindowNativeMethods.GetCurrentThreadId();
            uint foregroundThread = foreground != IntPtr.Zero
                ? InlineSearchWindowNativeMethods.GetWindowThreadProcessId(foreground, out _)
                : 0;

            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

                Activate();
                TxtSearchDisplay.Focus();
                Keyboard.Focus(TxtSearchDisplay);
                TxtSearchDisplay.CaretIndex = TxtSearchDisplay.Text.Length;

                if (foreground != IntPtr.Zero && foregroundThread != 0)
                {
                    _originalLayout = InlineSearchWindowNativeMethods.GetKeyboardLayout(currentThread);
                    IntPtr layout = InlineSearchWindowNativeMethods.GetKeyboardLayout(foregroundThread);
                    if (layout != IntPtr.Zero)
                    {
                        InlineSearchWindowNativeMethods.ActivateKeyboardLayout(layout, 0);
                    }
                }

                return IsActive && TxtSearchDisplay.IsKeyboardFocusWithin;
            }
            finally
            {
                if (attached)
                    InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        public void HideWindow() => _manager.CloseInlineSearch();

        public void UpdateSearchDisplay(string text)
        {
            _searchText = text;
            TxtSearchDisplay.Text = text;
            TxtSearchDisplay.CaretIndex = TxtSearchDisplay.Text.Length;
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            _viewModel.SearchQuery = text;
        }

        public void UpdateActionsLayout() => _inputHandler.UpdateActionsLayout();

        public void LaunchByShortcutIndex(int num) => _inputHandler.LaunchByShortcutIndex(num);

        public void OpenFileOrFolderExternal(string path) => InlineSearchNavigator.OpenFileOrFolderExternal(this, path);

        public void LocateInExplorerExternal(string path) => InlineSearchNavigator.LocateInExplorerExternal(this, path);

        public void ExecuteSearchResult(AppSearchResult result) => InlineSearchNavigator.ExecuteSearchResult(this, result);

        public bool IsPointInsideWindowExternal(int x, int y)
        {
            return InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y);
        }

        private void HandleActiveWindowMoved()
        {
            if (IsVisible)
            {
                _positioner.PositionWindow();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _manager.ExplorerTracker.OnActiveWindowMoved -= HandleActiveWindowMoved;
            _menuPresenter.Dispose();
            if (_originalLayout != IntPtr.Zero)
            {
                InlineSearchWindowNativeMethods.ActivateKeyboardLayout(_originalLayout, 0);
                _originalLayout = IntPtr.Zero;
            }
            base.OnClosed(e);
        }
    }
}
