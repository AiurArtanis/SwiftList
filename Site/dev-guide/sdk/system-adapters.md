# System & Dialog Adapters

These interfaces let a plugin integrate SwiftList with *other* windows — File Explorer, native
file-picker dialogs, third-party file managers — rather than just its own search windows.

## `IActivePathCollector`

Extracts the "current directory" from whatever foreground window is active, so SwiftList knows
what to scope a search to (or resolve a relative action against).

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // localized name of the app/manager this targets
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

Both the active (focused) element and its containing window are passed in separately, since many
file managers put the actual path in a child control (an address bar, a tree view selection) that
isn't the top-level window itself.

## `IFileDialogAdapter`

Reads and drives natively-rendered Windows Open/Save file dialogs, so SwiftList can be embedded
into them (see [`IInlineSearchAdapter`](#iinlinesearchadapter) below) and keep them in sync.

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

## `IInlineSearchAdapter`

Embeds a SwiftList search bar directly into a target file dialog or file explorer window (the
"inline window" from the User Manual), keeping selection in sync in both directions.

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // default false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: delegates to CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // optional
    void OnSelectionChanged(IntPtr hwnd, string path);    // optional
    void OnSearchFinished(IntPtr hwnd, bool executed);    // optional
}
```

`AdapterRect` (shared with `IFileDialogAdapter`) is a plain `{ Left, Top, Right, Bottom }` `int`
rectangle.

## `IQuickNavigationProvider`

Fires on double-click or middle-click in empty space inside a supported window (see
[Hotkeys → Quick navigation](../../user-guide/hotkeys#quick-navigation-mouse)) — typically used to
pop a cascaded menu or trigger navigation from the desktop or an explorer window.

```csharp
interface IQuickNavigationProvider
{
    bool CanShow(
        IntPtr activeHwnd, string processName, string className,
        bool isDesktop, int x, int y, MouseTriggerType triggerType);
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`MouseTriggerType` is `DoubleClick` or `MiddleClick`. `DynamicMenuItem` is the same model used by
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider).
