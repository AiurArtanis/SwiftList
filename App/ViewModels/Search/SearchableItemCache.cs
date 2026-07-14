using System.Collections.Concurrent;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search;

// Owns the per-provider load/cache lifecycle for ISearchableItemProvider -- split out of
// SearchableItemMapper purely to keep that file under the file-length limit; SearchableItemMapper
// still owns the actual query-matching and AppSearchResult-building logic.
internal static class SearchableItemCache
{
    public sealed record CacheEntry(SearchableItem Item, List<string> Aliases, System.Windows.Media.ImageSource? Icon);

    private static readonly ConcurrentDictionary<string, List<CacheEntry>> _cache = new();
    private static readonly ConcurrentDictionary<string, Task> _loadingTasks = new();
    private static readonly ConcurrentDictionary<string, bool> _subscribed = new();

    // A cached CacheEntry bakes each provider's translated Title/Description into a plain string at
    // load time (see EnsureLoaded) -- unlike XAML's indexer bindings, that snapshot has no way to
    // notice a later language switch on its own. Providers only invalidate their own cache entry via
    // ItemsChanged, which most never fire for a language change (it's meant for the provider's own
    // underlying data changing, e.g. Start Menu file-system events) -- so without this, every cached
    // provider's item text stays frozen in whatever language was active the first time it loaded.
    static SearchableItemCache() => TranslationManager.Instance.PropertyChanged += (_, _) =>
                                         {
                                             _cache.Clear();
                                             _loadingTasks.Clear();
                                         };

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items -- there is no synchronous "wait for everything" alternative without
    // blocking the UI. Instead, a live search re-runs itself once more providers become available, so
    // results stream in rather than staying incomplete for the rest of the session. Raised on a
    // background thread; subscribers must marshal back to the UI thread themselves.
    public static event Action? ProviderLoaded;

    public static void Preload()
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
            EnsureLoaded(provider);
    }

    public static bool TryGetEntries(string providerId, out List<CacheEntry> entries) => _cache.TryGetValue(providerId, out entries!);

    public static void Invalidate(string providerId)
    {
        _cache.TryRemove(providerId, out _);
        _loadingTasks.TryRemove(providerId, out _);
    }

    public static void EnsureLoaded(ISearchableItemProvider provider)
    {
        var id = provider.GetType().Name;
        if (_subscribed.TryAdd(id, true))
        {
            provider.ItemsChanged += () => Invalidate(id);
        }

        if (_cache.ContainsKey(id)) return;

        _loadingTasks.GetOrAdd(id, _ => Task.Run(() =>
        {
            try
            {
                var rawItems = provider.GetSearchableItems() ?? Array.Empty<SearchableItem>();
                var entries = new List<CacheEntry>();
                foreach (var item in rawItems)
                {
                    if (item == null) continue;
                    var aliases = provider.EnableAlias
                        ? Core.AliasProviderRegistry.GetActiveProviders()
                            .Where(p => p.CanHandle(item.Title))
                            .SelectMany(p => p.GetAliases(item.Title))
                            .ToList()
                        : new List<string>();
                    entries.Add(new CacheEntry(item, aliases, MaterializeIcon(item)));
                }
                _cache[id] = entries;
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[SearchableItemCache] Error loading from provider '{provider.Name}': {ex.Message}", Core.LogLevel.Error);
                _cache[id] = new List<CacheEntry>();
            }
            finally
            {
                ProviderLoaded?.Invoke();
            }
        }));
    }

    // Convert a provider's raw GDI HBITMAP into a frozen, thread-safe BitmapSource ONCE at load time,
    // then release the GDI handle immediately. Providers hand us HBitmapIcon under a "caller must
    // DeleteObject" contract; materializing + freeing here avoids leaking one GDI handle per cached
    // item (which scales with the number of installed apps) and avoids rebuilding the bitmap on every
    // keystroke.
    private static System.Windows.Media.ImageSource? MaterializeIcon(SearchableItem item)
    {
        var hBitmap = item.HBitmapIcon;
        if (hBitmap == IntPtr.Zero) return null;
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            DeleteObject(hBitmap);
            item.HBitmapIcon = IntPtr.Zero;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // True when `targetFileFilterKind` (e.g. "FileFilter_tf") corresponds to an actually-registered
    // file filter, i.e. some loaded provider has an item with that ResultKind. Used to decide whether
    // a keyword search is a real filter prefix that should hide general items.
    public static bool IsRegisteredFilterKeyword(string targetFileFilterKind)
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            if (TryGetEntries(provider.GetType().Name, out var entries) &&
                entries.Any(e => string.Equals(e.Item.ResultKind, targetFileFilterKind, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }
}
