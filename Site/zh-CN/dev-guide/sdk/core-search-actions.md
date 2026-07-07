# 核心检索与动作

## `IPlugin`

唯一必须实现的接口——每个插件都要实现它，再加上其他按需实现的接口。

```csharp
interface IPlugin
{
    string Name { get; } // 本地化的显示名称
}
```

## 贡献搜索结果

### `ISearchableItemProvider`

返回一份完整的、可缓存的条目列表，供索引使用——适合内容是静态的或者枚举较慢、但不会随每次按键
变化的场景(例如开始菜单快捷方式、书签列表)。

```csharp
interface ISearchableItemProvider
{
    string Id { get; }     // 稳定的、与语言无关的标识符
    string Name { get; }
    bool EnableAlias { get; } // 默认 true
    event EventHandler? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

每次按键都会运行一次，直接返回结果——适合像计算器、URL 快捷方式这类"结果形状由查询本身决定"的内
容，而不是需要提前建好索引的东西。

```csharp
interface IInstantResultProvider
{
    string Id { get; }
    string Name { get; }
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // 可选的匹配高亮
}
```

### `IAliasProvider`

为非 ASCII 文本生成额外的可搜索字符串——中文文件名的拼音别名就是这样实现的(见
[PinyinAlias](../examples#pinyinalias-中文文件名拼音别名))。

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IEnumerable<string> GetAliases(string text);
}
```

### `IQueryTokenProvider`

从查询里认领一个尾部 token(例如 `report :size`)，并对已经匹配好的结果列表做变换——排序、过滤，
或者在一次普通搜索之上做其他组合处理。

```csharp
interface IQueryTokenProvider
{
    string Id { get; }
    string Name { get; }
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 结果上的动作

### `IActionProvider`

插件用来暴露静态和动态动作的容器接口:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicProviders();
}
```

### `ISearchResultAction`

一个单独的静态动作(例如"复制路径")，出现在动作菜单或快速窗口的动作热键里:

```csharp
interface ISearchResultAction
{
    string Id { get; }
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // 可选的默认热键
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

在运行时构建菜单项，而不是返回一份固定列表——真正的 Windows Shell 右键菜单(含级联子菜单)之所
以能出现在 SwiftList 的动作菜单里，用的就是这个机制；参见
[ShellMenuActionProvider](../examples#coreextensions-——-动作与-shell-右键菜单)。

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

## 支持模型

- **`SearchableItem`** / **`InstantResultItem`** —— Title、Description、IconData、IconColor、
  ActionType(`"Copy"` / `"Execute"` / `"None"`)、ActionArgument、TabCompletion。
- **`DynamicMenuItem`** —— Text、CommandId、IsSeparator、HasSubMenu、SubMenuHandle、IsDisabled、
  HBitmapItem、OnExecute、ShortcutHint。
- **`SearchWindowType`** 枚举 —— `Main`、`Quick`、`Inline`。可以让动作或提供者根据当前显示在
  [用户手册](../../user-guide/getting-started#三种窗口)里说的三种窗口的哪一种而表现不同。
