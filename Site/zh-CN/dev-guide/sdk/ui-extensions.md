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

`SidebarFilterGroup` 有一个 `Header`、一个 `AllowMultiSelect` 开关(默认 `false`;打开后这个分组
允许同时选中多项,用 OR 组合——如果分组里的选项只在单选时才有意义(比如互相重叠/累进的日期区
间),就不要打开它),以及一份 `SidebarFilterItem` 列表(Id、DisplayName、可选图标，以及一个可选
的、对当前结果列表做异步过滤的 `FilterPredicate`)。宿主会在分组有选中项时自动显示一个清空按钮,
所以 provider 不需要自己维护一个"全部"/"任意"伪选项。

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

## 初始面板

### `IStartupPanelTabProvider`

给快速窗口的"初始面板"贡献一个标签——搜索框为空时结果列表上方显示的那个标签栏(见
[初始面板](../../user-guide/settings/startup-panel))。CoreExtensions 的历史记录和收藏夹两个标
签都是基于这个接口做的;参见[插件示例](../examples#coreextensions-——-动作与-shell-右键菜单)。

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IEnumerable<ISearchResult> GetItems();
}
```

`GetItems()` 在面板每次激活时都会同步调用，预期要快、不做 I/O——每次搜索框被清空都会调它一次，
不会做缓存。如果没有返回任何项目，这个标签会被整个排除在标签栏之外，而不是显示成空的。用户可以在
实时面板里用 **×** 按钮单独隐藏一个标签，这和在设置 → 插件里把该组件整个禁用是两回事，故意分开
处理——宿主程序使用组件的具体类型名称（`GetType().Name`）作为稳定 Key 来持久化隐藏状态。

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
    bool RendersExternally { get; } // 默认 false
}
```

两个可选的配套接口可以进一步优化预览行为:

- **`IPreviewSessionAware`** —— 如果预览提供者自身持有开销较大的进程外资源(托管的原生处理程
  序、文件锁)，就在预览提供者本身上实现这个接口;`EndPreviewSession()` 只在整个预览会话结束时
  调用一次，而不是每次切换预览目标都调用。唯一的例外:如果这个 provider 的 `RendersExternally`
  为 true，宿主会在每次从它切换走的时候都调用一次，不只是会话真正结束的时候——见下文。
- **`IReusablePreview`** —— 如果 `CreatePreview` 返回的 `UIElement` 能够重新指向一个新文件，而
  不需要从头重建，就在它上面实现这个接口:`TrySetTarget(path, isDir)` 返回 `true` 表示已经原地
  处理好了变更，返回 `false` 则告诉宿主需要重新构建一个新的预览。

`RendersExternally` 适用于真正的预览内容渲染在一个独立的、由外部管理的窗口里、而不是
`CreatePreview` 返回的那个 `UIElement` 上的场景——比如把文件整个交给另一个应用程序去处理。当胜
出的 provider 设置了这个属性，宿主会隐藏自己的预览面板，而不是显示 `CreatePreview` 的内容(反正
也不会真的显示出来，所以可以随便返回一个占位用的空内容)。配合 **`IReceivesPreviewPanelBounds`**
使用，可以拿到宿主自己那个预览面板本该占据的屏幕矩形(物理像素)，这样外部窗口就能被摆到那个位
置，而不是随便出现在别的地方:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

内置的(实验性)QuickLook 桥接插件就是一个真实例子:它通过命名管道探测一个外部的
[QuickLook](https://github.com/QL-Win/QuickLook) 应用，如果能连上，就把它的窗口停靠到宿主面板
原本的位置，覆盖所有文件/文件夹——具体的用户可见行为见[动作菜单与预览 → 通过 QuickLook 的外部
预览](../../user-guide/actions-and-preview#通过-quickLook-的外部预览可选)。注意这和 SwiftList
自己内置的预览面板是两回事——本代码库和文档里也习惯把那个内置面板非正式地称为"QuickLook"。

### `IThumbnailProvider`

覆盖匹配结果显示的图标/缩略图。

```csharp
interface IThumbnailProvider : IPluginComponent
{
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
