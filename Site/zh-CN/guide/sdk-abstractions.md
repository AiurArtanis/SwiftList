# 数据模型与辅助契约

此类接口留在 `Abstractions` 根命名空间中，作为插件运行所需的数据模型契约或交互句柄。它们**不作为插件在组件管理面板中显示**，而是作为服务插件的“寄生组件”或“交互媒介”。

---

## 1. `ISearchResult` (搜索结果条目) {#isearchresult}
代表一条在主搜索窗口中被检索出来的结果。

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

## 2. `ISearchResultAction` (搜索结果动作契约) {#isearchresultaction}
声明某个动作的具体展示特征与执行逻辑。

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
* **Keywords**：快捷搜索命令触发关键字。
* **IsVisibleInSearch**：该动作在普通输入匹配时是否直接在搜索列表中作为独立项显示。
* **IsVisibleInMenu**：该动作是否显示在右键的上下文菜单中。
* **Execute**：具体的点击动作响应逻辑，通过传入的 `IPluginSearchWindow` 句柄可以控制主窗口隐藏或刷新。

---

## 3. `IPluginSearchWindow` (搜索视窗句柄) {#ipluginsearchwindow}
主搜索窗口暴露给插件动作的操作接口。

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

## 4. `IConfigurable` (可配置声明接口) {#iconfigurable}
如果插件需要持久化的用户配置界面，可以实现此接口，向主程序提供表单配置 Schema。

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
    void OnConfigChanged(string fieldId, object value);
}
```

---

## 5. `ITheme` (主题模型定义) {#itheme}
主题插件暴露给 WPF `ResourceDictionary` 的基础契约。

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

## 6. SDK 共享系统服务 {#sdk-services}
SDK 提供了一组静态服务类与助手包装器。插件可以调用这些服务与宿主系统进行通信、注册监听目录、读取全局配置、检索收藏夹或访问历史记录。

### 6.1 `DirectoryIndexerService` (托管目录索引服务)
允许插件注册自定义文件夹路径，由主程序进行统一的后台索引维护与实时的文件变化（USN/FileSystemWatcher）监听。

```csharp
public static class DirectoryIndexerService
{
    // 托管的索引目录内容发生变更时触发的事件
    public static event Action<string>? DirectoryChanged;

    // 向主程序注册一个待监控和索引的文件夹目录
    public static void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*");

    // 取消注册该插件名下的所有监控文件夹目录
    public static void UnregisterDirectories(string pluginId);

    // 对该插件名下注册的托管目录执行特定词匹配的文件检索
    public static Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default);
}
```

### 6.2 `PluginSettingsService` (插件配置读取服务)
提供对用户在设置面板中为该插件保存的自定义表单数据的只读访问。

```csharp
public static class PluginSettingsService
{
    // 读取指定键的配置，并自动反序列化为对应的强类型对象
    public static T GetSetting<T>(string pluginId, string key, T defaultValue);
}
```

### 6.3 `FavoritesService` (系统收藏夹服务)
向插件暴露用户在 SwiftList 中收藏的所有快捷文件夹与文件列表。

```csharp
public static class FavoritesService
{
    // 获取全局收藏夹目录数据的方法委托
    public static Func<IEnumerable<FavoriteItem>>? GetFavoritesFunc { get; set; }
}
```

### 6.4 `HistoryService` (历史记录服务)
允许插件读取用户的历史打开记录，以便于做匹配项相关性分值上下文提权。

```csharp
public static class HistoryService
{
    // 获取用户最近打开历史文件的委托方法
    public static Func<IEnumerable<string>>? GetHistoryPathsFunc { get; set; }
}
```

### 6.5 `IconService` (图标提取服务)
允许插件动态获取系统外壳缓存中的文件或文件夹图标（返回 WPF `ImageSource`）。

```csharp
public static class IconService
{
    // 获取指定路径的文件/文件夹的系统图标
    public static ImageSource? GetIcon(string path, bool isDir);
}
```

### 6.6 `TranslationService` (多语言辅助服务)
一个解耦的辅助工具，用于方便插件在运行时自动加载嵌入程序集的 JSON 翻译字典。

```csharp
public static class TranslationService
{
    // 根据键值查找翻译
    public static string Get(string key);

    // 获取格式化的翻译文本
    public static string Format(string key, params object[] args);

    // 扫描程序集获取支持的语言文化代码
    public static IReadOnlyList<string> GetSupportedCultures(Assembly assembly);

    // 加载嵌入程序集的 JSON 翻译文件并反序列化为字典
    public static Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName);
}
```

### 6.7 `Logger` (系统日志服务)
提供给插件输出日志的静态工具，主程序会自动收集并打印至调试窗口或日志文件中。

```csharp
public static class Logger
{
    // 打印特定等级的日志 (LogLevel 包括 Error, Warn, Info, Debug)
    public static void Log(string message, LogLevel level = LogLevel.Info);
}
```

