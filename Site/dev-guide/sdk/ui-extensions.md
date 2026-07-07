# UI & Preview Extensions

## Result display

### `ISidebarFilterProvider`

Adds categorizing filter groups to the results sidebar (e.g. date-range or size buckets).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` has a `Header` and a list of `SidebarFilterItem`s (Id, DisplayName, optional
icon, and an optional async `FilterPredicate` over the current result list).

### `IResultColumnProvider`

Injects extra columns into the results grid view (file size, modified date, custom metadata, ...).

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` carries a column id, header text, width, and optional
`VisibilityPredicate`/`SortComparer` delegates.

## Preview & thumbnails

### `IFilePreviewProvider`

Renders a custom WPF `UIElement` in the QuickLook preview pane (see
[Actions Menu & Preview](../../user-guide/actions-and-preview#quicklook-preview)) for file types
you want to handle specially.

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
}
```

Two optional companion interfaces refine preview behavior:

- **`IPreviewSessionAware`** — implement this on the preview provider itself if it holds onto
  expensive out-of-process resources (a hosted native handler, a file lock); `EndPreviewSession()`
  is called once the whole preview session ends, not on every individual preview swap.
- **`IReusablePreview`** — implement this on the `UIElement` returned from `CreatePreview` if it
  can re-point itself at a new file instead of being rebuilt from scratch: `TrySetTarget(path,
  isDir)` returns `true` if it handled the change in place, `false` to tell the host to build a
  fresh preview instead.

### `IThumbnailProvider`

Overrides the icon/thumbnail shown for matching results.

```csharp
interface IThumbnailProvider
{
    string Id { get; }
    string Name { get; }
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

## Themes & localization

### `IThemeProvider` / `ITheme`

Registers one or more custom WPF resource dictionaries as selectable themes (shown in
**Settings → General → Interface theme**).

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
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

Supplies UI strings for a given culture — for the plugin's own UI, or (as with `PinyinAlias`) just
its own display name. See [Example Plugins](../examples) for a plugin that implements this
alongside an unrelated interface on the same class.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations` (see [Host Services](./services)) is the standard way
to back this with JSON files embedded in your plugin DLL.
