using System.Windows.Threading;
using SwiftList.Core;
using SwiftList.Core.Hook;
using Application = System.Windows.Application;
using SwiftList.App.ViewModels.Search;
using SwiftList.App.Views.InlineSearchWindow.Helpers;

namespace SwiftList.App.Services;

/// <summary>
/// Manages the lifecycle of InlineSearchWindow and keeps hooks persistent
/// so the window can be created and destroyed dynamically on user input.
/// </summary>
public class InlineSearchManager : IDisposable
{
    private static InlineSearchManager? _instance;
    public static InlineSearchManager Instance => _instance ??= new InlineSearchManager();

    private InlineSearchWindow? _window;
    private readonly ExplorerTracker _explorerTracker;
    private readonly KeyboardHookService _keyboardHook;
    private readonly MouseHookService _mouseHook;
    private string _searchText = string.Empty;
    private IntPtr _currentHostHwnd = IntPtr.Zero;

    public ExplorerTracker ExplorerTracker => _explorerTracker;
    public KeyboardHookService KeyboardHook => _keyboardHook;
    public MouseHookService MouseHook => _mouseHook;
    public string SearchText => _searchText;

    private InlineSearchManager()
    {
        _explorerTracker = new ExplorerTracker();
        _keyboardHook = new KeyboardHookService(_explorerTracker);
        _mouseHook = new MouseHookService(IsPointInsideWindow);

        if (App.HookClient != null)
        {
            App.HookClient.OnExplorerActivated += (hwnd, title, className, isDesktop) => _explorerTracker.UpdateActiveWindow(hwnd, title, className, isDesktop);
            App.HookClient.OnExplorerDeactivated += () => _explorerTracker.DeactivateWindow();
            App.HookClient.OnPathCaptured += (path, isDesktop) => _explorerTracker.UpdatePath(path, isDesktop);
            App.HookClient.OnActiveWindowMoved += () => _explorerTracker.MoveActiveWindow();
            App.HookClient.OnError += msg => _explorerTracker.RaiseErrorExternal(msg);
        }

        WireUpExplorerEvents();
        WireUpMouseEvents();
        WireUpKeyboardEvents();
    }

    public void Start()
    {
        _keyboardHook.Start();
        Logger.Log("[InlineSearchManager] Services started.", LogLevel.Debug);
    }

    private bool IsPointInsideWindow(int x, int y)
    {
        if (_window == null || !_window.IsVisible) return false;
        return _window.IsPointInsideWindowExternal(x, y);
    }

    private void WireUpExplorerEvents()
    {
        _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null && _currentHostHwnd == hwnd)
                {
                    return;
                }

                if (_explorerTracker.IsActiveWindowDialog)
                {
                    CloseInlineSearch("ExplorerActivated (Dialog)");
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(string.Empty);
                }
                else
                {
                    CloseInlineSearch("ExplorerActivated (Non-Dialog)");
                }
            }));

        _explorerTracker.OnExplorerDeactivated += () => Application.Current.Dispatcher.BeginInvoke(new Action(() => CloseInlineSearch("ExplorerDeactivated")));

        _explorerTracker.OnError += (msg) => Logger.Log($"[InlineSearchManager] ExplorerTracker error: {msg}", LogLevel.Error);

        _explorerTracker.OnPathCaptured += (path, isDesktop) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null)
                {
                    var oldScope = _window.ViewModel.SearchScope;
                    if (oldScope != path)
                    {
                        _window.ViewModel.SearchScope = path;
                        Logger.Log($"[InlineSearchManager] Updated SearchScope dynamically to: {path}", LogLevel.Debug);

                        if (string.IsNullOrEmpty(_window.SearchText))
                            _window.ViewModel.Search.PerformSearch(string.Empty);
                    }
                }
                else if (_explorerTracker.IsActiveWindowDialog)
                {
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(string.Empty);
                }
            }));
    }

    private void WireUpMouseEvents() => _mouseHook.OnClickOutside += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                 {
                                                     if (_explorerTracker.IsActiveWindowDialog)
                                                         return;
                                                     CloseInlineSearch("ClickOutside");
                                                 }));

    private void WireUpKeyboardEvents()
    {
        var router = new InlineSearchKeyboardEventRouter(
            _keyboardHook,
            getWindow: () => _window,
            onCharacterTyped: ch =>
            {
                if (ch != '\0')
                {
                    _searchText += ch;
                }
                EnsureWindowCreated();
                _window?.UpdateSearchDisplay(_searchText);
            },
            onBackspacePressed: () =>
            {
                if (_searchText.Length > 0)
                {
                    _searchText = _searchText.Substring(0, _searchText.Length - 1);
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(_searchText);
                }
            });

        router.Wire();
    }

    private void EnsureWindowCreated()
    {
        if (_window != null) return;

        var viewModel = new QuickSearchViewModel();
        var scope = _explorerTracker.ActivePath;
        if (_explorerTracker.ActiveInlineAdapter != null && _explorerTracker.ActiveHwnd != IntPtr.Zero)
        {
            scope = _explorerTracker.ActiveInlineAdapter.GetSearchScope(_explorerTracker.ActiveHwnd);
        }
        viewModel.SearchScope = scope;
        viewModel.IsInlineSearchContext = true;

        _window = new InlineSearchWindow(viewModel, this);
        _currentHostHwnd = _explorerTracker.ActiveHwnd;
        _keyboardHook.IsInlineSearchVisible = true;
        _mouseHook.Start();

        _window.Show();

        var fgHwnd = ExplorerNativeHooks.GetForegroundWindow();
        var isTextInputFocused = fgHwnd != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(fgHwnd);

        if (!isTextInputFocused && !_explorerTracker.IsActiveWindowDialog)
        {
            // Try to activate and focus synchronously first while we are still in the input/hook processing thread context
            if (_window.ActivateAndFocusSearchBox())
            {
                _keyboardHook.IsInlineSearchVisible = false;
                _keyboardHook.Stop();
            }
            else
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_window == null || !_window.IsVisible)
                    {
                        return;
                    }

                    if (_window.ActivateAndFocusSearchBox())
                    {
                        _keyboardHook.IsInlineSearchVisible = false;
                        _keyboardHook.Stop();
                    }
                }), DispatcherPriority.Input);
            }
        }
        else
        {
            // If a text input is already focused, show the window without stealing focus,
            // and restore focus to the edit box.
            var dialogHwnd = _explorerTracker.ActiveHwnd;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dialogHwnd != IntPtr.Zero)
                {
                    ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                    var editBox = ExplorerNativeHooks.FindSubEditBox(dialogHwnd);
                    if (editBox != IntPtr.Zero)
                        ExplorerNativeHooks.SetFocus(editBox);
                }
            }), DispatcherPriority.Input);
        }

        Logger.Log($"[InlineSearchManager] Created and shown new InlineSearchWindow. Scope: {viewModel.SearchScope}", LogLevel.Debug);
    }

    public bool IsExecuting { get; set; }

    public void CloseInlineSearch(string reason = "Unknown")
    {
        if (_window == null) return;

        if (_explorerTracker.ActiveInlineAdapter != null && _explorerTracker.ActiveHwnd != IntPtr.Zero)
        {
            try
            {
                _explorerTracker.ActiveInlineAdapter.OnSearchFinished(_explorerTracker.ActiveHwnd, IsExecuting);
            }
            catch (Exception ex)
            {
                Logger.Log($"[InlineSearchManager] Error calling OnSearchFinished: {ex.Message}", LogLevel.Error);
            }
        }
        IsExecuting = false;

        _mouseHook.Stop();
        _keyboardHook.IsInlineSearchVisible = false;
        _keyboardHook.Start();
        _searchText = string.Empty;

        var win = _window;
        _window = null;
        _currentHostHwnd = IntPtr.Zero;
        win.Hide();
        win.Close();

        Logger.Log($"[InlineSearchManager] InlineSearchWindow closed and destroyed. Reason: {reason}", LogLevel.Debug);
    }



    public bool IsInlineSearchActive => _window != null && _window.IsVisible;

    public void FocusSearchBox()
    {
        if (_window != null && _window.IsVisible)
        {
            if (_explorerTracker.IsActiveWindowDialog 
                && _window.SearchBox.SearchTextBox.IsKeyboardFocusWithin 
                && string.IsNullOrEmpty(_window.SearchText))
            {
                _window.ResetInlineSearchAndFocusDialog();
                return;
            }
            _window.ActivateAndFocusSearchBox();
        }
    }

    public void Dispose()
    {
        CloseInlineSearch("Dispose");
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _explorerTracker.Dispose();
    }
}
