using System.IO;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

public class FileSizeFilterProvider : ISidebarFilterProvider
{
    public int SortOrder => 3;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var group = new SidebarFilterGroup
        {
            Header = TranslationService.Get("Filter_SizeHeader")
        };

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_All",
            DisplayName = TranslationService.Get("Filter_SizeAny"),
            IconData = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.58 8 8-3.58 8-8 8zm0-11c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z",
            FilterPredicate = res => true
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Small",
            DisplayName = TranslationService.Get("Filter_SizeSmall"),
            IconData = "M12 13.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z",
            FilterPredicate = res =>
            {
                if (res.IsDir) return false;
                try
                {
                    var fi = new FileInfo(res.FullPath);
                    return fi.Exists && fi.Length < 1 * 1024 * 1024;
                }
                catch { return false; }
            }
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Medium",
            DisplayName = TranslationService.Get("Filter_SizeMedium"),
            IconData = "M12 14.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z",
            FilterPredicate = res =>
            {
                if (res.IsDir) return false;
                try
                {
                    var fi = new FileInfo(res.FullPath);
                    return fi.Exists && fi.Length >= 1 * 1024 * 1024 && fi.Length <= 100 * 1024 * 1024;
                }
                catch { return false; }
            }
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Large",
            DisplayName = TranslationService.Get("Filter_SizeHuge"),
            IconData = "M12 16.5c-2.48 0-4.5-2.02-4.5-4.5s2.02-4.5 4.5-4.5 4.5 2.02 4.5 4.5-2.02 4.5-4.5 4.5z",
            FilterPredicate = res =>
            {
                if (res.IsDir) return false;
                try
                {
                    var fi = new FileInfo(res.FullPath);
                    return fi.Exists && fi.Length > 100 * 1024 * 1024;
                }
                catch { return false; }
            }
        });

        return new[] { group };
    }
}
