# 核心检索与动作插件

此类接口定义了与 SwiftList 核心搜索、结果注入以及动作响应直接关联的插件模块。

---

## 1. `IPlugin` 与 `IActionProvider` {#iplugin}
插件的基础接口被重构为更单一职责的定义：`IPlugin` 作为所有插件的最基础标识接口，而 `IActionProvider` 则用于可选提供右键菜单动作。

```csharp
public interface IPlugin
{
    string Name { get; }
}

public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicProviders();
}
```
* **IPlugin**：插件的最基础接口，包含插件本地化名称 `Name`。
* **IActionProvider**：当插件需要扩展搜索结果的右键菜单、双击或快捷键响应事件时实现的接口。
  * `GetActions()`：返回该插件提供的静态动作实例列表（动作需实现 `ISearchResultAction` 接口）。
  * `GetDynamicProviders()`：返回该插件提供的动态动作提供者列表（如系统资源管理器右键菜单，需实现 `IDynamicActionProvider` 接口）。

---

## 2. `IAliasProvider` (别名提供器) {#ialiasprovider}
为非 ASCII 文字（如中文文件名）计算拼音首字母、全拼或其他自定义检索别名，以支持更智能的模糊搜索。

```csharp
public interface IAliasProvider
{
    string Name { get; }
    IEnumerable<string> GetAliases(string text);
}
```
* **GetAliases(string text)**：根据输入的文件名或标题，返回一组用于加速匹配的别名字符串。这些别名会在 Service 扫描阶段预先计算好，并伴随查询被动态检索。

---

## 3. `IInstantResultProvider` (即时结果计算) {#iinstantresultprovider}
提供在搜索栏直接计算并展现结果的机制（例如输入 `=1+1` 显示 `2`，或输入 `>cmd` 运行命令行）。

```csharp
public interface IInstantResultProvider
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    bool CanProvide(string query);
    IEnumerable<ISearchResult> GetResults(string query);
}
```
* **CanProvide**：判断当前查询条件（如前缀）是否由该提供器处理。
* **GetResults**：同步/即时返回渲染好的结果条目列表。

---

## 4. `ISearchableItemProvider` (静态/动态搜索源) {#isearchableitemprovider}
用于向主检索列表注册自定义条目（例如系统控制面板设置项、浏览器书签等）。

```csharp
public interface ISearchableItemProvider
{
    string Name { get; }
    bool EnableAlias => true;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```
* **GetSearchableItems()**：返回自定义条目数组。该列表在主程序启动时会被统一缓存和索引，支持全局快速搜索。
