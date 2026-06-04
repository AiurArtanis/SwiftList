using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwiftList.App.Services
{
    public static class SearchResultFilter
    {
        private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".md", ".csv", ".ini", ".conf", ".log"
        };

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".ico", ".webp"
        };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm"
        };

        public static IEnumerable<AppSearchResult> Filter(IEnumerable<AppSearchResult> results, string typeFilter, string dateFilter)
        {
            var filtered = results;

            // 1. Filter by Type
            if (typeFilter == "Folder")
            {
                filtered = filtered.Where(r => !r.IsApplication && r.IsDir);
            }
            else if (typeFilter == "File")
            {
                filtered = filtered.Where(r => !r.IsApplication && !r.IsDir);
            }
            else if (typeFilter == "Doc")
            {
                filtered = filtered.Where(r => !r.IsApplication && !r.IsDir && DocExts.Contains(Path.GetExtension(r.FullPath)));
            }
            else if (typeFilter == "Image")
            {
                filtered = filtered.Where(r => !r.IsApplication && !r.IsDir && ImageExts.Contains(Path.GetExtension(r.FullPath)));
            }
            else if (typeFilter == "Video")
            {
                filtered = filtered.Where(r => !r.IsApplication && !r.IsDir && VideoExts.Contains(Path.GetExtension(r.FullPath)));
            }

            // 2. Filter by Date Modified
            if (dateFilter != "All" && int.TryParse(dateFilter, out int days))
            {
                var cutoff = DateTime.Now.AddDays(-days);
                filtered = filtered.Where(r => r.DateModified >= cutoff);
            }

            return filtered;
        }
    }
}
