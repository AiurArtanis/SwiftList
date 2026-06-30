using System.Diagnostics;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.ProcessManager;

public class ProcessManagerInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("ProcessManager_Name");

    private static string GetProcessPath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return TranslationService.Get("ProcessManager_AccessDenied");
        }
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var trimmed = query.Trim();
        var isPsQuery = string.Equals(trimmed, "ps", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("ps ", StringComparison.OrdinalIgnoreCase);

        if (!isPsQuery)
            yield break;

        var searchTerm = "";
        if (trimmed.StartsWith("ps ", StringComparison.OrdinalIgnoreCase))
        {
            searchTerm = trimmed.Substring(3).Trim();
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            yield break;
        }

        var matches = new List<Process>();
        var searchTermLower = searchTerm.ToLowerInvariant();

        foreach (var proc in processes)
        {
            try
            {
                if (string.IsNullOrEmpty(searchTerm))
                {
                    matches.Add(proc);
                }
                else
                {
                    var pidStr = proc.Id.ToString();
                    var name = proc.ProcessName;

                    if (name.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase) ||
                        pidStr.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(proc);
                    }
                }
            }
            catch
            {
                // Process might have already exited
            }
        }

        // Sort alphabetically by process name
        matches.Sort((a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase));

        // Limit results to 100 items to keep search extremely snappy
        var results = matches.Take(100);

        var pathKey = TranslationService.Get("ProcessManager_Path");
        var windowKey = TranslationService.Get("ProcessManager_Window");

        foreach (var proc in results)
        {
            var pid = 0;
            var processName = "Unknown";
            var windowTitle = "";

            try
            {
                pid = proc.Id;
                processName = proc.ProcessName;
                windowTitle = proc.MainWindowTitle;
            }
            catch
            {
                continue;
            }

            var path = GetProcessPath(proc);
            var title = $"{processName}.exe (PID: {pid})";
            var desc = string.IsNullOrWhiteSpace(windowTitle)
                ? $"{pathKey}: {path}"
                : $"{windowKey}: {windowTitle} | {pathKey}: {path}";

            var hasRealIcon = !string.IsNullOrEmpty(path) && !path.StartsWith("[");

            yield return new InstantResultItem
            {
                Title = title,
                Description = desc,
                IconData = hasRealIcon ? $"path:{path}" : "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
                IconColor = hasRealIcon ? null : "AccentRed",
                ActionType = "Execute",
                ActionArgument = $"kill:{pid}",
                TabCompletion = $"ps {processName}"
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var trimmed = query.Trim();
        var mask = new bool[text.Length];
        if (!trimmed.StartsWith("ps ", StringComparison.OrdinalIgnoreCase)) return mask;

        var searchTerm = trimmed.Substring(3).Trim();
        if (string.IsNullOrEmpty(searchTerm)) return mask;

        var textLower = text.ToLowerInvariant();
        var searchTermLower = searchTerm.ToLowerInvariant();

        var idx = textLower.IndexOf(searchTermLower, StringComparison.Ordinal);
        if (idx >= 0)
        {
            for (var i = idx; i < idx + searchTermLower.Length && i < mask.Length; i++)
            {
                mask[i] = true;
            }
        }
        return mask;
    }
}
