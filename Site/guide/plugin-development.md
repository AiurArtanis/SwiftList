# Plugin Development

SwiftList provides an SDK that allows developers to write C# class libraries extending search providers or alias resolvers.

## Plugin SDK Structure

All plugins only need to reference `SwiftList.PluginSdk` and implement key interfaces:

### 1. `ISearchableItemProvider` (Custom Search Providers)
Supplies custom items to the main search list (e.g. system settings, bookmarks, or web search APIs).
```csharp
public interface ISearchableItemProvider
{
    string Name { get; }
    bool EnableAlias => true;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 2. `IAliasProvider` (Alias Resolver)
Generates Pinyin, initials, or multi-lingual search aliases for items.
```csharp
public interface IAliasProvider
{
    IEnumerable<string> GetAliases(string text);
}
```

## Plugin Load Path
When the application starts, it scans `%APPDATA%\SwiftList\Plugins` and the `Plugins` folder in the executable's directory to load DLL files implementing SDK interfaces.
