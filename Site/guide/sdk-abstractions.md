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

---

## 6. Shared SDK Services {#sdk-services}
The SDK provides a collection of static service classes helper wrappers that plugins can invoke to interact with the host system, query user directories, get system favorites, or access history.

### 6.1 `DirectoryIndexerService` (Managed Indexer)
Allows plugins to register custom directories for automatic background indexing and real-time USN / file system monitoring.

```csharp
public static class DirectoryIndexerService
{
    // Event triggered when indexed directory content changes
    public static event Action<string>? DirectoryChanged;

    // Registers a directory to be indexed and monitored
    public static void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*");

    // Unregisters all directories registered by a plugin
    public static void UnregisterDirectories(string pluginId);

    // Queries files in registered directories of a plugin
    public static Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default);
}
```

### 6.2 `PluginSettingsService` (Settings Access)
Provides read-only access to custom fields defined by the plugin in the main settings window.

```csharp
public static class PluginSettingsService
{
    // Fetches settings value deserialized dynamically
    public static T GetSetting<T>(string pluginId, string key, T defaultValue);
}
```

### 6.3 `FavoritesService` (System Favorites)
Exposes the favorite items configured by the user in the core app.

```csharp
public static class FavoritesService
{
    // Retrieves user favorite directory listings
    public static Func<IEnumerable<FavoriteItem>>? GetFavoritesFunc { get; set; }
}
```

### 6.4 `HistoryService` (Search History)
Allows plugins to check historical run paths for context prioritization.

```csharp
public static class HistoryService
{
    // Retrieves list of recently ran items
    public static Func<IEnumerable<string>>? GetHistoryPathsFunc { get; set; }
}
```
