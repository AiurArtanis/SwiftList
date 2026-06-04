using System;
using System.Collections.Generic;
using System.IO;
using SwiftList.App.Services;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Providers
{
    public class FileSizeColumnProvider : IResultColumnProvider
    {
        public IEnumerable<ResultColumnDefinition> GetColumns()
        {
            return new[]
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
        }

        public string GetCellValue(ISearchResult result, string columnId)
        {
            if (result.IsDir)
            {
                return columnId == "Extension" ? TranslationService.Get("Column_TypeFolder") : string.Empty;
            }

            try
            {
                if (columnId == "FileSize")
                {
                    var fi = new FileInfo(result.FullPath);
                    if (!fi.Exists) return string.Empty;
                    return FormatSize(fi.Length);
                }
                
                if (columnId == "Extension")
                {
                    string ext = Path.GetExtension(result.FullPath).ToUpper();
                    return string.IsNullOrEmpty(ext)
                        ? TranslationService.Get("Column_TypeFile")
                        : TranslationService.Format("Column_TypeExtFile", ext.TrimStart('.'));
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double doubleBytes = bytes;
            int i = 0;
            while (doubleBytes >= 1024 && i < suffixes.Length - 1)
            {
                doubleBytes /= 1024;
                i++;
            }
            return $"{doubleBytes:0.##} {suffixes[i]}";
        }
    }
}
