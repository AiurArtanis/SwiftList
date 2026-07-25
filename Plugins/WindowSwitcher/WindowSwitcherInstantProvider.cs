using System.Diagnostics;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.WindowSwitcher;

public class WindowSwitcherInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WindowSwitcher_Name");

    // Falls back to the default even if an empty string was already persisted before RequireNonEmpty
    // started enforcing this at save time -- an empty keyword should never silently make this
    // unreachable. Same defensive pattern as ProcessManagerInstantProvider.GetTriggerKeyword.
    private static string GetTriggerKeyword()
    {
        var value = PluginSettingsService.GetSetting("SwiftList.Plugins.WindowSwitcher", "TriggerKeyword", "win");
        return string.IsNullOrWhiteSpace(value) ? "win" : value;
    }

    private static string GetProcessPath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return TranslationService.Get("WindowSwitcher_AccessDenied");
        }
    }

    // Lower tier ranks first: 0 = literal window-title match, 1 = literal process-name match, 2 =
    // literal PID match, 3 = fuzzy/alias fallback on title or process name. Title ranks first (unlike
    // ProcessManagerInstantProvider's own process-name-first tiering) since switching is a title-driven
    // task -- title is usually what tells apart two windows of the same app (e.g. two Explorer windows).
    // Returns null when nothing matches at any tier.
    internal static int? GetMatchTier(string title, string processName, string pid, string searchTerm)
    {
        if (!string.IsNullOrEmpty(title) && title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (processName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (pid.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (!string.IsNullOrEmpty(title) && FuzzyMatchService.IsMatch(searchTerm, title))
            return 3;
        if (FuzzyMatchService.IsMatch(searchTerm, processName))
            return 3;

        return null;
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
            yield break;

        var trimmed = query.Trim();
        var isTriggered = string.Equals(trimmed, keyword, StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase);

        if (!isTriggered)
            yield break;

        var searchTerm = "";
        if (trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
        {
            searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        }

        List<WindowEnumerator.SwitchableWindow> windows;
        try
        {
            windows = WindowEnumerator.GetSwitchableWindows();
        }
        catch
        {
            yield break;
        }

        var matches = new List<(WindowEnumerator.SwitchableWindow Window, string ProcessName, string Path, int Tier)>();

        foreach (var window in windows)
        {
            string processName;
            string path;
            try
            {
                using var proc = Process.GetProcessById(window.ProcessId);
                processName = proc.ProcessName;
                path = GetProcessPath(proc);
            }
            catch
            {
                // The window's owning process exited between enumeration and this lookup.
                continue;
            }

            if (string.IsNullOrEmpty(searchTerm))
            {
                matches.Add((window, processName, path, 0));
                continue;
            }

            var tier = GetMatchTier(window.Title, processName, window.ProcessId.ToString(), searchTerm);
            if (tier.HasValue)
                matches.Add((window, processName, path, tier.Value));
        }

        // Lower tier first (stronger match), then alphabetically by window title within the same tier.
        matches.Sort((a, b) =>
        {
            var tierCompare = a.Tier.CompareTo(b.Tier);
            return tierCompare != 0 ? tierCompare : string.Compare(a.Window.Title, b.Window.Title, StringComparison.OrdinalIgnoreCase);
        });

        // Limit results to keep search snappy, same reasoning as ProcessManagerInstantProvider.
        var results = matches.Take(50);

        var pathKey = TranslationService.Get("WindowSwitcher_Path");

        foreach (var (window, processName, path, _) in results)
        {
            var hasRealIcon = !string.IsNullOrEmpty(path) && !path.StartsWith("[");

            yield return new InstantResultItem
            {
                Title = window.Title,
                Description = $"{processName}.exe | {pathKey}: {path}",
                // Generic window-rectangle glyph (Material "web_asset") when the real exe icon can't be
                // resolved (access denied, or the process exited between enumeration and this point).
                IconData = hasRealIcon ? $"path:{path}" : "M21 3H3c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H3V5h18v14z",
                IconColor = hasRealIcon ? null : "AccentBlue",
                ActionType = "Execute",
                ActionArgument = $"activatewindow:{window.Handle.ToInt64()}",
                TabCompletion = $"{keyword} {window.Title}"
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        var trimmed = query.Trim();
        var mask = new bool[text.Length];
        if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)) return mask;

        var searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        if (string.IsNullOrEmpty(searchTerm)) return mask;

        return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? mask;
    }
}
