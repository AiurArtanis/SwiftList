# Core Search & Action Plugins

These interfaces define plugin modules directly associated with SwiftList's core search capabilities, item indexing, and action execution.

---

## 1. `IAction` (Action Extension) {#iaction}
Used to extend context menus on search results or respond to double-clicks and hotkey triggers.

```csharp
public interface IAction
{
    string Name { get; }
    IEnumerable<ISearchResultAction> GetActions();
}
```
* **Name**: The display name of the action group in the plugin manager.
* **GetActions()**: Returns a list of concrete actions provided by this plugin (each must implement the `ISearchResultAction` interface).

---

## 2. `IAliasProvider` (Alias Resolver) {#ialiasprovider}
Generates initials, Pinyin, or customized lookup aliases for non-ASCII text to enable smarter fuzzy search.

```csharp
public interface IAliasProvider
{
    string Name { get; }
    IEnumerable<string> GetAliases(string text);
}
```
* **GetAliases(string text)**: Returns a collection of search aliases calculated for a file or item title.

---

## 3. `IInstantResultProvider` (Instant Result calculation) {#iinstantresultprovider}
Calculates and renders instant results directly in the query box (e.g., typing `=1+1` displays `2`, or `>cmd` runs terminal actions).

```csharp
public interface IInstantResultProvider
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    bool CanProvide(string query);
    IEnumerable<ISearchResult> GetResults(string query);
}
```
* **CanProvide**: Determines whether this provider handles the query pattern.
* **GetResults**: Synchronously returns instant results.

---

## 4. `ISearchableItemProvider` (Custom Databases) {#isearchableitemprovider}
Registers custom items into the global search index (e.g. system settings, browser bookmarks).

```csharp
public interface ISearchableItemProvider
{
    string Name { get; }
    bool EnableAlias => true;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```
* **GetSearchableItems()**: Returns searchable items which are cached and indexed at main application startup.
