# 界面扩展与展示插件

此类接口负责扩展 SwiftList 主窗口的视图排版、表格字段、左侧分类、快速预览及主题语言配色。

---

## 1. `ISidebarFilterProvider` (侧边栏过滤器) {#isidebarfilterprovider}
向主界面左侧边栏添加分类过滤器（如按特定文件大小、后缀类型分组）。

```csharp
public interface ISidebarFilterProvider
{
    IEnumerable<FilterGroup> GetFilterGroups();
}
```

---

## 2. `IResultColumnProvider` (结果列提供器) {#iresultcolumnprovider}
为搜索结果的 ListView 网格追加自定义展示列（例如文件占用大小、修改日期等列信息）。

```csharp
public interface IResultColumnProvider
{
    IEnumerable<ColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

---

## 3. `IFilePreviewProvider` (文件预览提供器) {#ifilepreviewprovider}
在 QuickLook（空格键预览）窗口中，针对选中的文件类型提供定制的 WPF 渲染界面。

```csharp
public interface IFilePreviewProvider
{
    string Name { get; }
    int Priority => 0;
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
}
```

---

## 4. `ITranslationProvider` (语言翻译提供器) {#itranslationprovider}
向主程序及其他插件提供多语言翻译文件的字典支持。

```csharp
public interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyCollection<string> SupportedCultures { get; }
    string? Translate(string key, string culture);
}
```

---

## 5. `IThemeProvider` (主题提供器) {#ithemeprovider}
向主程序注册并提供 WPF 的自定义资源字典（XAML 样式主题）。

```csharp
public interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}
```
* **GetThemes()**：返回一个或多个 `ITheme` 主题包列表。
