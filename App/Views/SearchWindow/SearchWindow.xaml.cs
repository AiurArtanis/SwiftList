using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftList.App.ViewModels;
using SwiftList.App.Views.SearchWindow;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using ListView = System.Windows.Controls.ListView;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using SwiftList.App.ViewModels.Search;

namespace SwiftList.App
{
    public partial class SearchWindow : Window, ISearchWindow
    {
        private readonly SearchViewModel _viewModel;
        private readonly SearchWindowChromeHandler _chromeHandler;
        private readonly SearchWindowInputHandler _inputHandler;
        private readonly ShellMenuPresenter _menuPresenter;

        public SearchWindow(string initialQuery = "")
        {
            InitializeComponent();
            
            _menuPresenter = new ShellMenuPresenter(this);
            _chromeHandler = new SearchWindowChromeHandler(this);
            _inputHandler = new SearchWindowInputHandler(this);

            this.PreviewKeyDown += Window_PreviewKeyDown;
            this.StateChanged += SearchWindow_StateChanged;

            // Restrict window size when maximized to avoid covering the Windows Taskbar
            this.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
            this.MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;

            _viewModel = new SearchViewModel(initialQuery);
            this.DataContext = _viewModel;

            // Dynamically load custom GridView columns from ResultColumnProviders
            var gridView = LstGridResults.View as GridView;
            if (gridView != null)
            {
                foreach (var provider in PluginManager.Instance.ResultColumnProviders)
                {
                    foreach (var colDef in provider.GetColumns())
                    {
                        var gvc = new GridViewColumn
                        {
                            Header = colDef.HeaderText,
                            Width = colDef.Width
                        };

                        var binding = new System.Windows.Data.Binding($"[{colDef.ColumnId}]")
                        {
                            Mode = System.Windows.Data.BindingMode.OneWay
                        };

                        var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                        textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
                        textBlockFactory.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
                        textBlockFactory.SetValue(TextBlock.ForegroundProperty, new System.Windows.DynamicResourceExtension("TextSecondary2"));
                        textBlockFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
                        textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

                        gvc.CellTemplate = new DataTemplate { VisualTree = textBlockFactory };
                        gridView.Columns.Add(gvc);
                    }
                }
            }

            _viewModel.FilteredResults.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LstGridResults.Items.Count > 0)
                    {
                        LstGridResults.SelectedIndex = 0;
                        LstGridResults.ScrollIntoView(LstGridResults.SelectedItem);
                    }
                    else
                    {
                        LstGridResults.SelectedIndex = -1;
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            };

            this.Loaded += (s, e) =>
            {
                this.Activate();
                this.Focus();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TxtSearchBox.Focus();
                    Keyboard.Focus(TxtSearchBox);
                    if (initialQuery != null)
                    {
                        TxtSearchBox.SelectionStart = initialQuery.Length;
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            };

            // Actions list double-click and preview click event registration
            LstActions.MouseDoubleClick += _menuPresenter.HandleActionsMouseDoubleClick;
            LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;
        }

        // ==========================================
        // ISearchWindow Interface Implementation
        // ==========================================
        public UIElement ResultsPanel => GridSearchResults;
        ListBox ISearchWindow.LstResults => LstGridResults;
        Grid ISearchWindow.GridSearchResults => GridSearchResults;
        Grid ISearchWindow.GridActions => GridActions;
        TextBlock ISearchWindow.TxtActionsTarget => TxtActionsTarget;
        ListBox ISearchWindow.LstActions => LstActions;
        public string SearchText => TxtSearchBox.Text;
        public void UpdateActionsLayout() { /* Fixed-size window, no dynamic resizing needed */ }

        public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
        public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
        public void HideWindow() => this.Close();

        // ==========================================
        // Window Control Exposures for Handlers
        // ==========================================
        public TextBox TxtSearchBoxControl => TxtSearchBox;
        public ListView LstGridResultsControl => LstGridResults;
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
                if (!string.IsNullOrWhiteSpace(TxtSearchBox.Text))
                {
                    query = TxtSearchBox.Text;
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
                var scrollViewer = FindScrollViewer(LstGridResults);
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

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader headerClicked)
            {
                if (headerClicked.Column != null)
                {
                    string headerText = headerClicked.Column.Header as string ?? string.Empty;
                    if (!string.IsNullOrEmpty(headerText))
                    {
                        // Strip existing arrows from header text to get the actual clean name
                        string cleanHeader = headerText.Replace(" ▲", "").Replace(" ▼", "");
                        
                        _viewModel.SortByColumn(cleanHeader);

                        // Update all headers in the GridView to display the arrow for the sorted column only
                        var gridView = LstGridResults.View as GridView;
                        if (gridView != null)
                        {
                            foreach (var col in gridView.Columns)
                            {
                                if (col.Header is string colHeaderText)
                                {
                                    string cleanColHeader = colHeaderText.Replace(" ▲", "").Replace(" ▼", "");
                                    if (cleanColHeader == cleanHeader)
                                    {
                                        col.Header = cleanColHeader + (_viewModel.IsSortAscending ? " ▲" : " ▼");
                                    }
                                    else
                                    {
                                        col.Header = cleanColHeader;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private ScrollViewer? FindScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
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
            base.OnClosed(e);
        }
    }
}
