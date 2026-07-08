# 系统与对话框适配

这些接口让插件可以和*其他*窗口集成——文件资源管理器、原生文件选择对话框、第三方文件管理器——而
不仅仅是 SwiftList 自己的搜索窗口。

## `IActivePathCollector`

从当前活动的前台窗口中提取"当前目录"，让 SwiftList 知道该把搜索范围限定在哪里(或者相对什么路径
解析动作)。

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 目标应用/管理器的本地化名称
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

活动(获得焦点)的元素和它所在的窗口是分开传入的，因为很多文件管理器把实际路径放在子控件里(地址
栏、树形视图的选中项)，而不是顶层窗口本身。

## `IFileDialogAdapter`

读取并驱动原生渲染的 Windows 打开/保存文件对话框，让 SwiftList 可以被嵌入其中(见下面的
[`IInlineSearchAdapter`](#iinlinesearchadapter))并保持双方同步。

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 默认 true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

## `IInlineSearchAdapter`

把 SwiftList 搜索栏直接嵌入目标文件对话框或文件资源管理器窗口(即用户手册里说的"内嵌窗口")，双
向保持选中状态同步。

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // 默认 false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // 默认委托给 CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // 可选
    void OnSelectionChanged(IntPtr hwnd, string path);    // 可选
    void OnSearchFinished(IntPtr hwnd, bool executed);    // 可选
}
```

`AdapterRect`(与 `IFileDialogAdapter` 共用)是一个简单的 `{ Left, Top, Right, Bottom }` `int` 矩
形。

## `IQuickNavigationProvider`

为快速导航菜单提供内容(通常是级联菜单)——见
[热键 → 快速导航](../../user-guide/hotkeys#快速导航鼠标)。菜单该不该弹出由宿主决定，不是这个接口
的职责:任何已被 `IInlineSearchAdapter`/`IFileDialogAdapter` 识别的窗口，触发菜单的工作已经有人做
了，所以这个接口纯粹是内容来源。

```csharp
interface IQuickNavigationProvider
{
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`MouseTriggerType` 是 `DoubleClick` 或 `MiddleClick`。`DynamicMenuItem` 与
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider) 用的是同一个模型。
