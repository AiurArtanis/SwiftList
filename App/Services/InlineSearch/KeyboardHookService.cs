using SwiftList.Core.Hook;

using SwiftList.Core.Wire;
namespace SwiftList.App.Services;

public class KeyboardHookService : IDisposable
{
    private bool _isActive;

    public event Action<char>? OnCharacterTyped;
    public event Action? OnBackspacePressed;
    public event Action? OnEscapePressed;
    public event Action? OnEnterPressed;
    public event Action? OnUpPressed;
    public event Action? OnDownPressed;
    public event Action? OnLeftPressed;
    public event Action? OnRightPressed;
    public event Action<int>? OnCtrlNumberPressed;

    public bool IsActive => _isActive;

    private bool _isInlineSearchVisible;
    public bool IsInlineSearchVisible
    {
        get => _isInlineSearchVisible;
        set
        {
            _isInlineSearchVisible = value;
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.SetInlineSearchVisible, BoolVal = value });
        }
    }

    private bool _isQuickSearchWindowVisible;
    public bool IsQuickSearchWindowVisible
    {
        get => _isQuickSearchWindowVisible;
        set
        {
            _isQuickSearchWindowVisible = value;
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.SetQuickSearchVisible, BoolVal = value });
        }
    }

    public KeyboardHookService(ExplorerTracker tracker)
    {
        if (App.HookClient != null)
        {
            App.HookClient.OnCharacterTyped += ch => OnCharacterTyped?.Invoke(ch);
            App.HookClient.OnBackspacePressed += () => OnBackspacePressed?.Invoke();
            App.HookClient.OnEscapePressed += () => OnEscapePressed?.Invoke();
            App.HookClient.OnEnterPressed += () => OnEnterPressed?.Invoke();
            App.HookClient.OnUpPressed += () => OnUpPressed?.Invoke();
            App.HookClient.OnDownPressed += () => OnDownPressed?.Invoke();
            App.HookClient.OnLeftPressed += () => OnLeftPressed?.Invoke();
            App.HookClient.OnRightPressed += () => OnRightPressed?.Invoke();
            App.HookClient.OnCtrlNumberPressed += num => OnCtrlNumberPressed?.Invoke(num);
        }
    }

    public void Start() => _isActive = true;

    public void Stop() => _isActive = false;

    public void Dispose()
    {
    }
}
