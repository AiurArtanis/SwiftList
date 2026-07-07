using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

public class DateModifiedFilterProvider : ISidebarFilterProvider
{
    public int SortOrder => 2;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var group = new SidebarFilterGroup
        {
            Header = TranslationService.Get("Filter_DateHeader")
        };

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_All",
            DisplayName = TranslationService.Get("Filter_DateAny"),
            IconData = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
            FilterPredicate = results => Task.FromResult(results)
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_1",
            DisplayName = TranslationService.Get("Filter_Date1"),
            IconData = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
            FilterPredicate = results => FilterByDateAsync(results, DateTime.Now.AddDays(-1))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_7",
            DisplayName = TranslationService.Get("Filter_Date7"),
            IconData = "M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V10h14v10zm0-12H5V6h14v2z",
            FilterPredicate = results => FilterByDateAsync(results, DateTime.Now.AddDays(-7))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_30",
            DisplayName = TranslationService.Get("Filter_Date30"),
            IconData = "M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V10h14v10zm0-12H5V6h14v2z",
            FilterPredicate = results => FilterByDateAsync(results, DateTime.Now.AddDays(-30))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_365",
            DisplayName = TranslationService.Get("Filter_Date365"),
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-9 14H7v-7h3v7zm4 0h-3V7h3v10zm4 0h-3v-4h3v4z",
            FilterPredicate = results => FilterByDateAsync(results, DateTime.Now.AddDays(-365))
        });

        return new[] { group };
    }

    private static async Task<IReadOnlyList<ISearchResult>> FilterByDateAsync(IReadOnlyList<ISearchResult> results, DateTime cutoff)
    {
        var paths = results.Select(r => r.FullPath).Distinct().ToList();
        var metadata = await FileMetadataService.GetMetadataAsync(paths);
        return results.Where(r => metadata.TryGetValue(r.FullPath, out var m) && m.Modified >= cutoff).ToList();
    }
}
