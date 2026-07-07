# 共享抽象契约

其他 SDK 文档页面里用到的模型和支持性契约。

## `ISearchResult`

每一个插件接口操作的都是这份结果的只读视图——插件永远拿不到可变的结果对象，只有这个:

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

## `IPluginSearchWindow`

传给 `ISearchResultAction.Execute` 等回调的最小窗口控制接口——刻意保持精简;插件应该通过它来操作
结果，而不是持有真实窗口的引用:

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

和 `IPlugin` 一起实现这个接口，就能在**设置 → 插件 → 配置**里自动获得一个配置界面——简单场景下
不需要自己写 WPF。

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` 是一份扁平的 `Fields: List<PluginConfigField>`。每个 `PluginConfigField`
有一个 `Key`，可选的 `GroupKey`/`LabelKey`/`DescriptionKey`(翻译 key，如果你有自己的
`ITranslationProvider` 就通过它解析)，一个 `FieldType`，一个 `DefaultValue`，以及——取决于类型
——`Choices` 或嵌套的 `SubFields`。

`ConfigFieldType` 涵盖:`Boolean`、`Text`、`Integer`、`Choice`、`Array`、`Object`、`Group`、
`StringList`、`Hotkey`、`FilePath`、`FolderPath`。参见
[CoreExtensions](../examples#coreextensions-动作与-shell-右键菜单) 里一个用到嵌套分组和
`StringList` 的真实配置模式。

## 注册表

`ActivePathCollectorRegistry`、`FileDialogAdapterRegistry`、`InlineSearchAdapterRegistry` 是宿主
把所有已加载的对应[系统适配接口](./system-adapters)实现汇总到一处的方式。插件作者通常不需要直
接和这些注册表打交道——只要实现对应接口，宿主就会自动发现并注册你的插件。
