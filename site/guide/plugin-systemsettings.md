# System Settings Plugin (SystemSettings)

The System Settings Plugin is a built-in `ISearchableItemProvider` that indexes Windows Control Panel and "GodMode" tasks to expose them directly in the search bar.

## Implementation Details

The provider accesses the Windows **"GodMode" virtual folder CLSID**:
`shell:::{ED7BA470-8E54-465E-825C-99712043E01C}`

And enumerates its items dynamically via COM `Shell.Application`.

```csharp
public class SystemSettingsItemProvider : ISearchableItemProvider
{
    private const string GodModePath = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}";

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        dynamic dShell = Activator.CreateInstance(shellType);
        dynamic folder = dShell.NameSpace(GodModePath);
        
        foreach (var item in folder.Items())
        {
            // Extract item name, path, icon, and map to SearchableItem...
        }
    }
}
```

## Features
1. **Automated Multi-lingual Support**: Fetched names automatically match the OS language without requiring translation tables.
2. **Lightweight Caching**: Memory cached inside `SearchableItemMapper` upon the first search, running instantly on subsequent requests.
