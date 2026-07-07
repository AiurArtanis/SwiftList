# 界面与预览扩展

## 结果展示

### `ISidebarFilterProvider`

给结果侧栏添加分类过滤分组(例如日期区间或文件大小档位)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // 默认 100;数值越小越靠前渲染
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` 有一个 `Header` 和一份 `SidebarFilterItem` 列表(Id、DisplayName、可选图
标，以及一个可选的、对当前结果列表做异步过滤的 `FilterPredicate`)。

### `IResultColumnProvider`

给结果表格视图注入额外的列(文件大小、修改日期、自定义元数据等等)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` 携带列 id、表头文字、宽度，以及可选的 `VisibilityPredicate`/
`SortComparer` 委托。

## 预览与缩略图

### `IFilePreviewProvider`

在 QuickLook 预览面板里渲染自定义的 WPF `UIElement`(见
[动作菜单与预览 → QuickLook 预览](../../user-guide/actions-and-preview#quicklook-预览))，用于你
想特殊处理的文件类型。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // 默认 0;数值越大越先运行
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
}
```

两个可选的配套接口可以进一步优化预览行为:

- **`IPreviewSessionAware`** —— 如果预览提供者自身持有开销较大的进程外资源(托管的原生处理程
  序、文件锁)，就在预览提供者本身上实现这个接口;`EndPreviewSession()` 只在整个预览会话结束时
  调用一次，而不是每次切换预览目标都调用。
- **`IReusablePreview`** —— 如果 `CreatePreview` 返回的 `UIElement` 能够重新指向一个新文件，而
  不需要从头重建，就在它上面实现这个接口:`TrySetTarget(path, isDir)` 返回 `true` 表示已经原地
  处理好了变更，返回 `false` 则告诉宿主需要重新构建一个新的预览。

### `IThumbnailProvider`

覆盖匹配结果显示的图标/缩略图。

```csharp
interface IThumbnailProvider
{
    string Id { get; }
    string Name { get; }
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

## 主题与本地化

### `IThemeProvider` / `ITheme`

注册一个或多个自定义 WPF 资源字典，作为可选主题(显示在**设置 → 通用 → 界面主题**里)。

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // 默认 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

为给定文化提供界面字符串——可以是插件自己的界面文本，也可以像 `PinyinAlias` 那样，仅仅是它自己
的显示名称。参见[插件示例](../examples)了解一个把这个接口和另一个不相关接口实现在同一个类上的
插件。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // 例如 "zh-CN"、"en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`(见[宿主服务](./services))是用内嵌在插件 DLL 里
的 JSON 文件支撑这个接口的标准做法。
