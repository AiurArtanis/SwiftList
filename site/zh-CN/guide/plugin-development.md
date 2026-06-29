# 插件开发

SwiftList 提供了强大的 SDK，允许开发者通过编写 C# 类库的形式自定义各类搜索源或别名处理器。

## Plugin SDK 结构

所有的插件只需要引入 `SwiftList.PluginSdk` 依赖，并实现核心的接口：

### 1. `ISearchableItemProvider` (自定义搜索源)
用于向主搜索列表中提供各种自定义的条目（例如系统设置、浏览器书签、甚至网络搜索接口）。
```csharp
public interface ISearchableItemProvider
{
    string Name { get; }
    bool EnableAlias => true;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 2. `IAliasProvider` (别名扩展)
用于为搜索条目名计算拼音首字母、全拼或其他多语言别名。
```csharp
public interface IAliasProvider
{
    IEnumerable<string> GetAliases(string text);
}
```

## 插件加载路径
主程序启动时，会自动扫描 `%APPDATA%\SwiftList\Plugins` 以及主程序目录下的 `Plugins` 文件夹，并动态加载实现了 SDK 接口的 DLL 文件。
