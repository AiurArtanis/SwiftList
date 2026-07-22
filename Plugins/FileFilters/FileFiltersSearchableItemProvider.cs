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
        PluginSettingsService.SettingChanged += OnSettingChanged;
        ReloadFilters();
    }

    private void OnDirectoryChanged(string pluginId)
    {
        if (string.Equals(pluginId, "FileFilters", StringComparison.OrdinalIgnoreCase))
        {
            ItemsChanged?.Invoke();
        }
    }

    private void OnSettingChanged(string pluginId, string key)
    {
        if (string.Equals(pluginId, "SwiftList.Plugins.FileFilters", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key, "Filters", StringComparison.OrdinalIgnoreCase))
        {
            ReloadFilters();
            ItemsChanged?.Invoke();
        }
    }

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


        foreach (var filter in _registeredFilters)
        {
            if (filter.Folders == null) continue;

            // Prefix Name if present to display in description (e.g. "Movies · Z:\av")
            var filterPrefix = !string.IsNullOrWhiteSpace(filter.Name)
                ? $"{filter.Name.Trim()} · "
                : string.Empty;

            // Shared by both the file and folder loops below -- same SearchableItem shape either way,
            // only the default (no-keyword) ResultKind and the log message differ. UseShellExecute on a
            // directory path opens it in Explorer just as well as it opens a file with its default app,
            // so OnExecute needs no branching between the two.
            SearchableItem BuildItem(string path, string defaultResultKind)
            {
                var name = Path.GetFileName(path);
                var parentDir = Path.GetDirectoryName(path) ?? string.Empty;
                var desc = filterPrefix + parentDir;

                // Assign unique ResultKind code pattern for keyword routing isolation (e.g. "FileFilter_tf")
                var resultKind = string.IsNullOrEmpty(filter.Keyword) ? defaultResultKind : $"FileFilter_{filter.Keyword.Trim().ToLowerInvariant()}";

                return new SearchableItem
                {
                    Title = name, // Clean title
                    Description = desc,
                    ResultKind = resultKind,
                    HBitmapIcon = IntPtr.Zero, // Retain null so ShellIconHelper loads high fidelity video thumbnails dynamically!
                    ActionType = "None",
                    ActionArgument = path,
                    OnExecute = () =>
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            PluginSdk.Logger.Log($"[FileFilters] Failed to open '{path}': {ex.Message}", PluginSdk.LogLevel.Error);
                        }
                    }
                };
            }

            foreach (var root in filter.Folders.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                try
                {
                    // Scan directories for matching files
                    var files = Directory.EnumerateFiles(root, filter.FilterPattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (!string.IsNullOrEmpty(Path.GetFileName(file)))
                            items.Add(BuildItem(file, "File"));
                    }

                    // Subfolders themselves are also searchable -- unlike files, these are never
                    // filtered by FilterPattern (a pattern like "*.mp4" is meant for file extensions and
                    // would hide every folder if applied here too), so every folder under root is
                    // always included regardless of the configured pattern.
                    var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories);
                    foreach (var dir in directories)
                    {
                        if (!string.IsNullOrEmpty(Path.GetFileName(dir)))
                            items.Add(BuildItem(dir, "Directory"));
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
        PluginSettingsService.SettingChanged -= OnSettingChanged;
        DirectoryIndexerService.UnregisterDirectories("FileFilters");
        GC.SuppressFinalize(this);
    }
}
