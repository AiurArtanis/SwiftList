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

## Startup Panel

### `IStartupPanelTabProvider`

Contributes a tab to the quick window's Startup Panel — the tab strip shown above the result list
when the search box is empty (see [Startup Panel](../../user-guide/settings/startup-panel)).
CoreExtensions' History and Favorites tabs are both built on this; see
[Example Plugins](../examples#coreextensions-actions-and-the-shell-context-menu) for a walkthrough.

```csharp
interface IStartupPanelTabProvider
{
    string Id => GetType().Name; // stable key the enable/disable state persists against
    string Name { get; } // the tab's label
    IEnumerable<ISearchResult> GetItems();
}
```

`GetItems()` runs synchronously on every panel activation and is expected to be fast with no I/O —
it's called each time the search box is cleared, not cached. A tab that returns no items is left out
of the strip entirely rather than shown empty. The user can hide a tab from the live panel with its
**×** button independently of disabling the component altogether in Settings → Plugins — the two
are deliberately separate, so implementations shouldn't assume `Id` uniqueness means anything beyond
persisting that one piece of state.

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
