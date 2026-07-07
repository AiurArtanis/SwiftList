# Shared Abstractions

Models and support contracts used across the interfaces in the other SDK pages.

## `ISearchResult`

The read-only view of a result every plugin interface operates on — plugins never get a mutable
result object, only this:

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

The minimal window-control surface passed to `ISearchResultAction.Execute` and similar callbacks —
deliberately small; plugins act on results through this, not by holding onto the real window:

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

Implement this alongside `IPlugin` to get a configuration UI generated automatically under
**Settings → Plugins → Configure** — no custom WPF required for simple cases.

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` is a flat `Fields: List<PluginConfigField>`. Each `PluginConfigField` has a
`Key`, optional `GroupKey`/`LabelKey`/`DescriptionKey` (translation keys, resolved through your own
`ITranslationProvider` if you have one), a `FieldType`, a `DefaultValue`, and — depending on the
type — `Choices` or nested `SubFields`.

`ConfigFieldType` covers: `Boolean`, `Text`, `Integer`, `Choice`, `Array`, `Object`, `Group`,
`StringList`, `Hotkey`, `FilePath`, `FolderPath`. See
[CoreExtensions](../examples#coreextensions-actions-and-the-shell-context-menu) for a real schema
using nested groups and `StringList`.

## Registries

`ActivePathCollectorRegistry`, `FileDialogAdapterRegistry`, and `InlineSearchAdapterRegistry` are
how the host collects every loaded implementation of the corresponding
[system adapter interfaces](./system-adapters) into one place at runtime. Plugin authors don't
normally interact with these directly — implementing the interface is enough for the host to
discover and register your plugin automatically.
