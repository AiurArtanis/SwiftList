using Application = System.Windows.Application;

namespace SwiftList.App.Services;

/// <summary>
/// Routes keyboard hook events from <see cref="KeyboardHookService"/> to the active
/// <see cref="InlineSearchWindow"/>, keeping all navigation/input logic out of InlineSearchManager.
/// </summary>
internal sealed class InlineSearchKeyboardEventRouter
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly Func<InlineSearchWindow?> _getWindow;
    private readonly Action<char> _onCharacterTyped;
    private readonly Action _onBackspacePressed;

    public InlineSearchKeyboardEventRouter(
        KeyboardHookService keyboardHook,
        Func<InlineSearchWindow?> getWindow,
        Action<char> onCharacterTyped,
        Action onBackspacePressed)
    {
        _keyboardHook = keyboardHook;
        _getWindow = getWindow;
        _onCharacterTyped = onCharacterTyped;
        _onBackspacePressed = onBackspacePressed;
    }

    public void Wire()
    {
        _keyboardHook.OnCharacterTyped += ch => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    return;
                _onCharacterTyped(ch);
            }));



        _keyboardHook.OnBackspacePressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    return;
                _onBackspacePressed();
            }));

        _keyboardHook.OnEscapePressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    window.MenuPresenter.ExitActionsMode();
                else
                    InlineSearchManager.Instance.CloseInlineSearch();
            }));

        _keyboardHook.OnLeftPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    window.MenuPresenter.GoBackMenuOrExit();
            }));

        _keyboardHook.OnRightPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                    window.MenuPresenter.EnterSubMenu();
                else if (window.LstResults.SelectedItem is AppSearchResult result)
                    window.MenuPresenter.EnterActionsMode(result);
            }));

        _keyboardHook.OnEnterPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.ExecuteSelectedAction();
                    return;
                }

                if (window.LstResults.SelectedItem is AppSearchResult result)
                {
                    window.ExecuteSearchResult(result);
                }
                else if (window.LstResults.Items.Count > 0)
                {
                    window.LstResults.SelectedIndex = 0;
                    if (window.LstResults.SelectedItem is AppSearchResult firstResult)
                        window.ExecuteSearchResult(firstResult);
                }
            }));

        _keyboardHook.OnUpPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.NavigateActionsList(-1);
                    return;
                }

                if (window.LstResults.Items.Count > 0)
                {
                    var prev = window.LstResults.SelectedIndex - 1;
                    if (prev >= 0)
                        window.LstResults.SelectedIndex = prev;
                    window.LstResults.ScrollIntoView(window.LstResults.SelectedItem);
                }
            }));

        _keyboardHook.OnDownPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.NavigateActionsList(1);
                    return;
                }

                if (window.LstResults.Items.Count > 0)
                {
                    var next = window.LstResults.SelectedIndex + 1;
                    if (next < window.LstResults.Items.Count)
                        window.LstResults.SelectedIndex = next;
                    window.LstResults.ScrollIntoView(window.LstResults.SelectedItem);
                }
            }));

        _keyboardHook.OnCtrlNumberPressed += num => Application.Current.Dispatcher.BeginInvoke(new Action(() => _getWindow()?.LaunchByShortcutIndex(num)));
    }
}
