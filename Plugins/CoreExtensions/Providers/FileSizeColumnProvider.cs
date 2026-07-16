using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers;

public class FileSizeColumnProvider : IResultColumnProvider
{
    public IEnumerable<ResultColumnDefinition> GetColumns() => new[]
        {
            new ResultColumnDefinition
            {
                ColumnId = "FileSize",
                HeaderText = TranslationService.Get("Column_HeaderSize"),
                Width = 100
            },
            new ResultColumnDefinition
            {
                ColumnId = "Extension",
                HeaderText = TranslationService.Get("Column_HeaderType"),
                Width = 80
            }
        };

    public string GetCellValue(ISearchResult result, string columnId)
    {
        if (result.IsDir)
        {
            return columnId == "Extension" ? TranslationService.Get("Column_TypeFolder") : string.Empty;
        }

        if (columnId == "FileSize")
        {
            // Already known from the index via ISearchResult.Metadata -- no per-cell disk I/O.
            // Metadata.Modified == DateTime.MinValue means this result isn't file-index-backed (e.g.
            // a plugin-provided item), matching the old fi.Exists == false -> empty case.
            return result.Metadata.Modified == DateTime.MinValue ? string.Empty : FormatSize(result.Metadata.Size);
        }

        if (columnId == "Extension")
        {
            var ext = Path.GetExtension(result.FullPath).ToUpper();
            return string.IsNullOrEmpty(ext)
                ? TranslationService.Get("Column_TypeFile")
                : TranslationService.Format("Column_TypeExtFile", ext.TrimStart('.'));
        }

        return string.Empty;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double doubleBytes = bytes;
        var i = 0;
        while (doubleBytes >= 1024 && i < suffixes.Length - 1)
        {
            doubleBytes /= 1024;
            i++;
        }
        return $"{doubleBytes:0.##} {suffixes[i]}";
    }
}
