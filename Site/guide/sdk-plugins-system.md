# System & Navigation Plugins

These interfaces interact with Windows Shell, system focus, and common file dialogs.

---

## 1. `IActivePathCollector` (Path Collector) {#iactivepathcollector}
Extracts path location of the active folder window (e.g. File Explorer, Command Prompt, or Directory Opus).

```csharp
public interface IActivePathCollector
{
    string Name { get; }
    string? GetActivePath();
}
```
* **GetActivePath()**: Returns the physical directory path of the focused window. Returns `null` if the active window is unsupported.

---

## 2. `IFileDialogAdapter` (Dialog Controller) {#ifiledialogadapter}
Quickly reads and manipulates natively-rendered Windows Open/Save file dialogs.

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool IsDialogWindow(IntPtr hwnd, string className);
    string? GetDialogPath(IntPtr hwnd);
    bool SetDialogPath(IntPtr hwnd, string path);
}
```
* **IsDialogWindow**: Detects if the adapter supports the window handle and class name.
* **GetDialogPath** / **SetDialogPath**: Reads or modifies the address bar path of the active dialog.

---

## 3. `IInlineSearchAdapter` (Inline Search Docking) {#iinlinesearchadapter}
Embeds the SwiftList search bar directly into target third-party file picker dialogs.

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool Match(IntPtr hwnd, string processName, string className);
    bool Setup(IntPtr hwnd);
    void Unload(IntPtr hwnd);
    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
```

---

## 4. `IQuickNavigationProvider` (Mouse Navigation) {#iquicknavigationprovider}
Fires on desktop or explorer double-click or middle-click to trigger file dialog navigation or pop up cascaded menus.

```csharp
public interface IQuickNavigationProvider
{
    string Name { get; }
    bool CanShow(IntPtr activeHwnd, string processName, string className, bool isDesktop, int x, int y, MouseTriggerType triggerType);
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
}
```

---

## 5. `IDynamicActionProvider` (Dynamic Actions) {#idynamicactionprovider}
Generates dynamic context-menu items based on the active search selection (e.g., "Open in VS Code").

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
}
```
