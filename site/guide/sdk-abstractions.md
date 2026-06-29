# Models & Support Abstractions

These interfaces are located in the parent `Abstractions` namespace, representing core models, view handlers, or configurations. They **do not show up in the plugin manager**, but support the registered plugins.

---

## 1. `ISearchResult` (Search Result Model) {#isearchresult}
Represents a matched result entry in the main search page.

```csharp
public interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    bool IsDir { get; }
    string? ContextDirectory { get; }
}
```

---

## 2. `ISearchResultAction` (Action Contract) {#isearchresultaction}
Declares the presentation details and execution logic of an Action.

```csharp
public interface ISearchResultAction
{
    string Id { get; }
    string DisplayName { get; }
    string? Description => null;
    IReadOnlyCollection<string> Keywords => Array.Empty<string>();
    
    bool IsVisibleInSearch(ISearchResult result, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(ISearchResult result, SearchWindowType windowType) => Keywords.Count == 0;
    bool CanExecute(ISearchResult result);
    void Execute(ISearchResult result, IPluginSearchWindow view);
}
```

---

## 3. `IPluginSearchWindow` (Search Window Handle) {#ipluginsearchwindow}
Exposes control functions of the search window to plugin actions.

```csharp
public interface IPluginSearchWindow
{
    void HideSearchWindow();
    void ShowSearchWindow();
    void RefreshSearch();
    void SetSearchText(string text);
}
```

---

## 4. `IConfigurable` (Settings Schema) {#iconfigurable}
If a plugin needs persistent settings, it can implement this interface to tell the application its fields.

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
    void OnConfigChanged(string fieldId, object value);
}
```

---

## 5. `ITheme` (Theme Dictionary Model) {#itheme}
Exposes the WPF Resource Dictionary of a theme.

```csharp
public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();
    double WindowOpacity => 1.0;
}
```
