# 系统交互与适配插件

此类接口主要负责与 Windows 系统级外壳（Shell）、通用对话框以及系统焦点的交互。

---

## 1. `IActivePathCollector` (路径搜集器) {#iactivepathcollector}
获取当前操作系统中处于活跃状态的文件窗口路径（如 Windows 资源管理器、CMD 终端或 Directory Opus）。

```csharp
public interface IActivePathCollector
{
    string Name { get; }
    string? GetActivePath();
}
```
* **GetActivePath()**：返回当前焦点窗口所代表的本地物理路径。如果不支持当前窗口类型，返回 `null`。常用于快速保存、对话框导航等。

---

## 2. `IFileDialogAdapter` (文件对话框适配器) {#ifiledialogadapter}
用于适配和快捷控制 Windows 原生的“打开/保存文件”通用对话框。

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool IsDialogWindow(IntPtr hwnd, string className);
    string? GetDialogPath(IntPtr hwnd);
    bool SetDialogPath(IntPtr hwnd, string path);
}
```
* **IsDialogWindow**：根据窗口句柄和类名判定是否为该适配器支持的对话框。
* **GetDialogPath** / **SetDialogPath**：读取或强行修改对话框当前的地址栏路径。

---

## 3. `IInlineSearchAdapter` (嵌入式搜索适配器) {#iinlinesearchadapter}
在第三方窗口（如文件对话框）内部挂载并展示嵌入式的 SwiftList 极速搜索栏。

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
* **Match**：确定目标窗口是否需要挂载嵌入式搜索。
* **Setup / Unload**：初始化挂载或卸载搜索栏。
* **OnSelectionChanged / OnSearchFinished**：搜索项变更或结束时的回调。

---

## 4. `IQuickNavigationProvider` (鼠标快捷导航) {#iquicknavigationprovider}
在桌面或资源管理器空白处鼠标双击/中键点击时，触发路径快捷跳转或弹出级联导航菜单。

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

## 5. `IDynamicActionProvider` (动态动作提供器) {#idynamicactionprovider}
根据选中的搜索结果，动态生成上下文右键菜单操作（如“使用 VS Code 打开”）。

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
}
```
* **CanProvide**：判断该文件或项目是否支持此动态菜单。
* **GetMenuItems**：向 Windows 右键菜单句柄中注入自定义的菜单子项。
