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

### 6.5 `IconService` (Shell Icons)
Provides cached shell icon extraction for files and directories (returning a WPF `ImageSource`).

```csharp
public static class IconService
{
    // Retrieves cached shell icon for a path
    public static ImageSource? GetIcon(string path, bool isDir);
}
```

### 6.6 `TranslationService` (Dynamic Translations)
A helper utility to load embedded translation JSON dictionaries and resolve localized strings at runtime.

```csharp
public static class TranslationService
{
    // Gets translation by key
    public static string Get(string key);

    // Gets formatted translation by key
    public static string Format(string key, params object[] args);

    // Scans assembly for embedded translations
    public static IReadOnlyList<string> GetSupportedCultures(Assembly assembly);

    // Deserializes and loads embedded JSON translations
    public static Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName);
}
```

### 6.7 `Logger` (System Logging)
Allows plugins to log structured diagnostics back to the host application window and logs.

```csharp
public static class Logger
{
    // Logs a message with specific LogLevel (Error, Warn, Info, Debug)
    public static void Log(string message, LogLevel level = LogLevel.Info);
}
```

---

## 7. SDK Registries (Registries) {#sdk-registries}
The SDK provides a collection of static registry classes to register, retrieve, and filter loaded plugin adapters and collectors at runtime.

### 7.1 `ActivePathCollectorRegistry`
Manages all loaded active path collectors (`IActivePathCollector`).

```csharp
public static class ActivePathCollectorRegistry
{
    // Filter delegate to check if a collector is enabled
    public static Func<IActivePathCollector, bool> FilterFunc { get; set; }

    // Registers a new active path collector
    public static void Register(IActivePathCollector collector);

    // Retrieves all currently enabled active path collectors
    public static IReadOnlyList<IActivePathCollector> GetCollectors();

    // Retrieves all registered collectors regardless of whether they are enabled
    public static IReadOnlyList<IActivePathCollector> GetAllCollectors();
}
```

### 7.2 `FileDialogAdapterRegistry`
Manages all loaded file dialog adapters (`IFileDialogAdapter`).

```csharp
public static class FileDialogAdapterRegistry
{
    public static Func<IFileDialogAdapter, bool> FilterFunc { get; set; }
    public static void Register(IFileDialogAdapter adapter);
    public static IReadOnlyList<IFileDialogAdapter> GetAdapters();
    public static IReadOnlyList<IFileDialogAdapter> GetAllAdapters();
}
```

### 7.3 `InlineSearchAdapterRegistry`
Manages all loaded inline search adapters (`IInlineSearchAdapter`).

```csharp
public static class InlineSearchAdapterRegistry
{
    public static Func<IInlineSearchAdapter, bool> FilterFunc { get; set; }
    public static void Register(IInlineSearchAdapter adapter);
    public static IReadOnlyList<IInlineSearchAdapter> GetAdapters();
    public static IReadOnlyList<IInlineSearchAdapter> GetAllAdapters();
}
```

---

## 8. SDK Models (Models) {#sdk-models}

### 8.1 `FavoriteItem`
Represents a user's favorite shortcut item.

```csharp
public class FavoriteItem
{
    public string Name { get; set; }
    public string Path { get; set; }
}
```

### 8.2 `ListControlIpcBridge`
A static bridge class to communicate selection/item state between plugin hooks and host window lists.

```csharp
public static class ListControlIpcBridge
{
    public static Func<IntPtr, IEnumerable<string>>? GetListItemsFunc { get; set; }
    public static Func<IntPtr, string, IEnumerable<int>>? GetSelectedIndicesFunc { get; set; }
    public static Action<IntPtr, string, int, bool, bool>? SelectItemAction { get; set; }
    public static Action<IntPtr, string>? ClearSelectionAction { get; set; }
}
```

---

## 9. SDK Utility and Helper Classes (Helpers) {#sdk-helpers}

### 9.1 `ShellInvokeHelper`
SDK-level helper to execute Shell Namespace actions, especially virtual items like GodMode/Control Panel that cannot be launched directly via Process.Start.

```csharp
public static class ShellInvokeHelper
{
    public static void InvokeShellItem(string parentShellPath, string itemPath);
}
```

### 9.2 `ShellPathHelper`
Encapsulates Win32 Shell API invocations to resolve, convert, and inspect virtual shell folder paths, UNC paths, and Recycle Bin items.

### 9.3 `StartMenuShortcutResolver`
Helper class to resolve start menu shortcut link properties (.lnk / .url) including their actual target physical files and icons.

### 9.4 `UserProfileHelper`
A utility to retrieve and expand Windows user profile directories like `%USERPROFILE%` or app data folders.

### 9.5 `VectorIconHelper`
Helper to parse and transform WPF vector paths to render icons.


