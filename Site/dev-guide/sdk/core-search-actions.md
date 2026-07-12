# Core Search & Actions

## `IPlugin`

The one required interface — every plugin implements this, plus whichever others it needs.

```csharp
interface IPlugin
{
    string Name { get; } // Localized display name
}
```

## Contributing search results

### `ISearchableItemProvider`

Returns a full, cacheable list of items to fold into the index — for content that's static or
slow to enumerate but doesn't change every keystroke (e.g. Start Menu shortcuts, a bookmark list).

```csharp
interface ISearchableItemProvider
{
    string Id { get; }     // Stable, locale-independent identifier
    string Name { get; }
    bool EnableAlias { get; } // default true
    event EventHandler? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

Runs on every keystroke and returns results directly — for query-shaped content like a calculator
or a URL shortcut, not something you'd want indexed ahead of time.

```csharp
interface IInstantResultProvider
{
    string Id { get; }
    string Name { get; }
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // optional match highlighting
}
```

### `IAliasProvider`

Generates extra searchable strings for non-ASCII text — this is how pinyin aliasing for Chinese
filenames works (see [PinyinAlias](../examples#pinyinalias-pinyin-aliasing-for-chinese-filenames)).

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // default 1
    int[]? MapAliasToSourceIndices(string text, string alias); // default null
    void GetAliasesUtf8(string text, AliasByteSink dest); // default: adapts GetAliases
}
```

`Version`, `MapAliasToSourceIndices`, and `GetAliasesUtf8` are all default-implemented — most
providers never need to touch them:

- **`Version`**: bump it when this provider's output could change for the same input (an algorithm
  fix, a new rule, an updated data table). The index uses it to detect that previously-generated
  aliases from this provider are stale and need regenerating.
- **`MapAliasToSourceIndices`**: lets a match found against an alias (e.g. which pinyin letters
  matched) be translated back onto the original text for highlighting, instead of highlighting
  nothing because the query never appears verbatim in the untransliterated text. Return `null`
  (the default) if this alias wasn't produced by this provider for this text, or mapping isn't
  supported — the host treats that as "can't highlight via this provider," not an error.
- **`GetAliasesUtf8`**: byte-native variant used on the host's bulk indexing path, where aliases are
  ultimately stored as UTF-8 bytes. The default implementation adapts `GetAliases`, so existing
  providers work unchanged; only override it to skip string materialization entirely when your
  provider generates a very high volume of aliases and that allocation cost actually shows up in
  practice.

### `IQueryTokenProvider`

Claims a trailing token from the query (e.g. `report :size`) and transforms the already-matched
result list — sorting, filtering, or otherwise composing on top of a normal search.

```csharp
interface IQueryTokenProvider
{
    string Id { get; }
    string Name { get; }
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## Actions on results

### `IActionProvider`

The container a plugin implements to expose both static and dynamic actions:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

A single, static action (e.g. "Copy Path") shown in the Actions menu or the quick-window action
hotkeys:

```csharp
interface ISearchResultAction
{
    string Id { get; }
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // optional default hotkey
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

Builds menu items at runtime instead of returning a fixed list — this is how the real Windows
shell right-click menu (with nested cascade submenus) gets surfaced inside SwiftList's Actions
menu; see [ShellMenuActionProvider](../examples#coreextensions-actions-and-the-shell-context-menu).

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`Init()` is called by the host at most once per process, the first time any actions menu opens —
before `CanProvide`/`GetMenuItems` are actually invoked for any selection. The host guarantees the
at-most-once part, so an implementation doesn't need to guard against repeat calls itself. Use it
for slow one-time setup (e.g. warming up a native worker thread) that benefits from a genuine head
start instead of racing your own `CanProvide`/`GetMenuItems` call, which follow immediately after
with no lead time of their own — must not block, so do any real work on a background thread.
Default implementation is a no-op.

## Supporting models

- **`SearchableItem`** / **`InstantResultItem`** — Title, Description, IconData, IconColor,
  ActionType (`"Copy"` / `"Execute"` / `"None"`), ActionArgument, TabCompletion.
- **`DynamicMenuItem`** — Text, CommandId, IsSeparator, HasSubMenu, SubMenuHandle, IsDisabled,
  HBitmapItem, OnExecute, ShortcutHint.
- **`SearchWindowType`** enum — `Main`, `Quick`, `Inline`. Lets an action or provider behave
  differently depending on which of the three windows (see the
  [User Manual](../../user-guide/getting-started#the-three-windows)) it's showing in.
