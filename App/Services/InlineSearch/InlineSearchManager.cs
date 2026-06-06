using System;
using System.Windows;
using System.Windows.Threading;
using SwiftList.Core;
using SwiftList.Core.Hook;
using SwiftList.App.ViewModels;
using Application = System.Windows.Application;
using SwiftList.App.ViewModels.Search;

namespace SwiftList.App.Services
{
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
                App.HookClient.OnExplorerActivated += (hwnd, title, className, isDesktop) =>
                {
                    _explorerTracker.UpdateActiveWindow(hwnd, title, className, isDesktop);
                };
                App.HookClient.OnExplorerDeactivated += () =>
                {
                    _explorerTracker.DeactivateWindow();
                };
                App.HookClient.OnPathCaptured += (path, isDesktop) =>
                {
                    _explorerTracker.UpdatePath(path, isDesktop);
                };
                App.HookClient.OnActiveWindowMoved += () =>
                {
                    _explorerTracker.MoveActiveWindow();
                };
                App.HookClient.OnError += msg =>
                {
                    _explorerTracker.RaiseErrorExternal(msg);
                };
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
            _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_explorerTracker.IsActiveWindowDialog)
                    {
                        CloseInlineSearch();
                        EnsureWindowCreated();
                        _window?.UpdateSearchDisplay(string.Empty);
                    }
                    else
                    {
                        CloseInlineSearch();
                    }
                }));
            };

            _explorerTracker.OnExplorerDeactivated += () =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CloseInlineSearch();
                }));
            };

            _explorerTracker.OnError += (msg) =>
            {
                Logger.Log($"[InlineSearchManager] ExplorerTracker error: {msg}", SwiftList.Core.LogLevel.Error);
            };

            _explorerTracker.OnPathCaptured += (path, isDesktop) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
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
                }));
            };
        }

        private void WireUpMouseEvents()
        {
            _mouseHook.OnClickOutside += () =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_explorerTracker.IsActiveWindowDialog)
                        return;
                    CloseInlineSearch();
                }));
            };
        }

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
            string? scope = _explorerTracker.ActivePath;
            if (_explorerTracker.ActiveInlineAdapter != null && _explorerTracker.ActiveHwnd != IntPtr.Zero)
            {
                scope = _explorerTracker.ActiveInlineAdapter.GetSearchScope(_explorerTracker.ActiveHwnd);
            }
            viewModel.SearchScope = scope;
            viewModel.IsInlineSearchContext = true;

            _window = new InlineSearchWindow(viewModel, this);
            _keyboardHook.IsInlineSearchVisible = true;
            _mouseHook.Start();

            _window.Show();
            if (!_explorerTracker.IsActiveWindowDialog)
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
            else
            {
                // In dialog (file picker) mode: show window without stealing focus.
                // Immediately restore foreground to the dialog so the user can still
                // type in the file picker's edit box uninterrupted.
                IntPtr dialogHwnd = _explorerTracker.ActiveHwnd;
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (dialogHwnd != IntPtr.Zero)
                    {
                        ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                        IntPtr editBox = ExplorerNativeHooks.FindSubEditBox(dialogHwnd);
                        if (editBox != IntPtr.Zero)
                            ExplorerNativeHooks.SetFocus(editBox);
                    }
                }), DispatcherPriority.Input);
            }

            Logger.Log($"[InlineSearchManager] Created and shown new InlineSearchWindow. Scope: {viewModel.SearchScope}", LogLevel.Debug);
        }

        public bool IsExecuting { get; set; }

        public void CloseInlineSearch()
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
            win.Hide();
            win.Close();

            Logger.Log("[InlineSearchManager] InlineSearchWindow closed and destroyed.", LogLevel.Debug);
        }



        public void Dispose()
        {
            CloseInlineSearch();
            _keyboardHook.Dispose();
            _mouseHook.Dispose();
            _explorerTracker.Dispose();
        }
    }
}
