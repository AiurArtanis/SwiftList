# 数据模型与辅助契约

此类接口留在 `Abstractions` 根命名空间中，作为插件运行所需的数据模型契约或交互句柄。它们**不作为插件在组件管理面板中显示**，而是作为服务插件的“寄生组件”或“交互媒介”。

---

## 1. `ISearchResult` (搜索结果条目) {#isearchresult}
代表一条在主搜索窗口中被检索出来的结果。

```csharp
public interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    bool IsDir { get; }
    string? ContextDirectory { get; }
}
```

---

## 2. `ISearchResultAction` (搜索结果动作契约) {#isearchresultaction}
声明某个动作的具体展示特征与执行逻辑。

```csharp
public interface ISearchResultAction
{
    string Id { get; }
    string DisplayName { get; }
    string? Description => null;
    IReadOnlyCollection<string> Keywords => Array.Empty<string>();
    
    bool IsVisibleInSearch(ISearchResult result, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(ISearchResult result, SearchWindowType windowType) => Keywords.Count == 0;
    bool CanExecute(ISearchResult result);
    void Execute(ISearchResult result, IPluginSearchWindow view);
}
```
* **Keywords**：快捷搜索命令触发关键字。
* **IsVisibleInSearch**：该动作在普通输入匹配时是否直接在搜索列表中作为独立项显示。
* **IsVisibleInMenu**：该动作是否显示在右键的上下文菜单中。
* **Execute**：具体的点击动作响应逻辑，通过传入的 `IPluginSearchWindow` 句柄可以控制主窗口隐藏或刷新。

---

## 3. `IPluginSearchWindow` (搜索视窗句柄) {#ipluginsearchwindow}
主搜索窗口暴露给插件动作的操作接口。

```csharp
public interface IPluginSearchWindow
{
    void HideSearchWindow();
    void ShowSearchWindow();
    void RefreshSearch();
    void SetSearchText(string text);
}
```

---

## 4. `IConfigurable` (可配置声明接口) {#iconfigurable}
如果插件需要持久化的用户配置界面，可以实现此接口，向主程序提供表单配置 Schema。

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
    void OnConfigChanged(string fieldId, object value);
}
```

---

## 5. `ITheme` (主题模型定义) {#itheme}
主题插件暴露给 WPF `ResourceDictionary` 的基础契约。

```csharp
public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();
    double WindowOpacity => 1.0;
}
```
