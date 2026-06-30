# UI & Layout Plugins

These interfaces customize the main search panel UI, result columns, filters, previews, themes, and translations.

---

## 1. `ISidebarFilterProvider` (Sidebar Filter) {#isidebarfilterprovider}
Appends categorizing tags/filters in the left sidebar (e.g. file size ranges, modified dates).

```csharp
public interface ISidebarFilterProvider
{
    IEnumerable<FilterGroup> GetFilterGroups();
}
```

---

## 2. `IResultColumnProvider` (Custom Grid Columns) {#iresultcolumnprovider}
Injects customized ListView columns displaying additional file details (e.g. extension, formatted sizes).

```csharp
public interface IResultColumnProvider
{
    IEnumerable<ColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

---

## 3. `IFilePreviewProvider` (File Previews) {#ifilepreviewprovider}
Renders a custom WPF UIElement inside the QuickLook window when spacebar is pressed on supported files.

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

## 4. `ITranslationProvider` (Localizations) {#itranslationprovider}
Provides localization strings for components and plugins.

```csharp
public interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyCollection<string> SupportedCultures { get; }
    string? Translate(string key, string culture);
}
```

---

## 5. `IThemeProvider` (WPF Style Sheets) {#ithemeprovider}
Registers custom WPF resource dictionaries (XAML themes).

```csharp
public interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}
```

---

## 6. `IThumbnailProvider` (Custom File Thumbnails) {#ithumbnailprovider}
Provides custom file thumbnails or icon overrides for search results.

```csharp
public interface IThumbnailProvider
{
    string Id { get; }
    string Name { get; }
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```
* **CanProvideThumbnail**: Returns whether this provider can generate a thumbnail/icon for the specified path and directory state.
* **GetThumbnail**: Generates or retrieves the `ImageSource` for the file preview icon at the requested pixel size.
