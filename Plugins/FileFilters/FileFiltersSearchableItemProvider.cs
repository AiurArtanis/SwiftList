using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.FileFilters;

public class FileFiltersSearchableItemProvider : ISearchableItemProvider, IDisposable
{
    public string Id => "FileFiltersSearchableItemProvider";
    public string Name => TranslationService.Get("FileFilters_ProviderName");

    public event Action? ItemsChanged;

    public class FilterItem
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public List<string> Folders { get; set; } = new();
        public string FilterPattern { get; set; } = "*";
    }

    private readonly List<FilterItem> _registeredFilters = new();

    public FileFiltersSearchableItemProvider()
    {
        DirectoryIndexerService.DirectoryChanged += OnDirectoryChanged;
        ReloadFilters();
    }

    private void OnDirectoryChanged(string pluginId)
    {
        if (string.Equals(pluginId, "FileFilters", StringComparison.OrdinalIgnoreCase))
        {
            ItemsChanged?.Invoke();
        }
    }

    private string _lastConfigSignature = string.Empty;

    private void ReloadFilters()
    {
        // Unregister old ones
        DirectoryIndexerService.UnregisterDirectories("FileFilters");

        _registeredFilters.Clear();
        var filters = PluginSettingsService.GetSetting<List<FilterItem>>("SwiftList.Plugins.FileFilters", "Filters", null!);
        if (filters != null)
        {
            foreach (var f in filters.Where(x => x.Enabled))
            {
                _registeredFilters.Add(f);

                if (f.Folders != null)
                {
                    foreach (var path in f.Folders.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                    {
                        DirectoryIndexerService.RegisterDirectory("FileFilters", path, recursive: true, filterPattern: f.FilterPattern);
                    }
                }
            }
        }
    }

    public IEnumerable<SearchableItem> GetSearchableItems()
    {
        var items = new List<SearchableItem>();

        // Re-read settings config and serialize to check for changes
        var currentFilters = PluginSettingsService.GetSetting<List<FilterItem>>("SwiftList.Plugins.FileFilters", "Filters", null!);
        var signature = currentFilters != null
            ? System.Text.Json.JsonSerializer.Serialize(currentFilters)
            : string.Empty;

        if (signature != _lastConfigSignature)
        {
            _lastConfigSignature = signature;
            ReloadFilters();
        }

        foreach (var filter in _registeredFilters)
        {
            if (filter.Folders == null) continue;

            // Prefix Name if present to display in description (e.g. "Movies · Z:\av")
            var filterPrefix = !string.IsNullOrWhiteSpace(filter.Name)
                ? $"{filter.Name.Trim()} · "
                : string.Empty;

            foreach (var root in filter.Folders.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                try
                {
                    // Scan directories for matching files
                    var files = Directory.EnumerateFiles(root, filter.FilterPattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file);
                        if (string.IsNullOrEmpty(name)) continue;

                        var parentDir = Path.GetDirectoryName(file) ?? string.Empty;
                        var desc = filterPrefix + parentDir;

                        // Assign unique ResultKind code pattern for keyword routing isolation (e.g. "FileFilter_tf")
                        var resultKind = string.IsNullOrEmpty(filter.Keyword) ? "File" : $"FileFilter_{filter.Keyword.Trim().ToLowerInvariant()}";

                        items.Add(new SearchableItem
                        {
                            Title = name, // Clean title
                            Description = desc,
                            ResultKind = resultKind,
                            HBitmapIcon = IntPtr.Zero, // Retain null so ShellIconHelper loads high fidelity video thumbnails dynamically!
                            ActionType = "None",
                            ActionArgument = file,
                            OnExecute = () =>
                            {
                                try
                                {
                                    var psi = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = file,
                                        UseShellExecute = true
                                    };
                                    System.Diagnostics.Process.Start(psi);
                                }
                                catch (Exception ex)
                                {
                                    PluginSdk.Logger.Log($"[FileFilters] Failed to open file '{file}': {ex.Message}", PluginSdk.LogLevel.Error);
                                }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    PluginSdk.Logger.Log($"[FileFilters] Error scanning directory '{root}': {ex.Message}", PluginSdk.LogLevel.Warn);
                }
            }
        }

        return items;
    }

    public void Dispose()
    {
        DirectoryIndexerService.DirectoryChanged -= OnDirectoryChanged;
        DirectoryIndexerService.UnregisterDirectories("FileFilters");
        GC.SuppressFinalize(this);
    }
}
