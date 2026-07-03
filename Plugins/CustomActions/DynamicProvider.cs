using System.Diagnostics;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CustomActions;

public class DynamicProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("CustomActions_GroupName") ?? "自定义动作";

    private const string DefaultIcon =
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    public class ActionItem
    {
        public bool Enabled { get; set; } = true;
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string WorkingDir { get; set; } = string.Empty;
        public bool RunSilently { get; set; } = false;
        public bool RunAsAdmin { get; set; } = false;
        public string Hotkey { get; set; } = string.Empty;
        public bool FolderOnly { get; set; } = false;
        public string Extensions { get; set; } = string.Empty;
    }

    // ponytail: permanent cache; UserSettings.Load() is already memory-cached but GetSetting
    // still deserializes JSON every call. Cache here so deserialization happens once per session.
    // Invalidated on ClearSession() (called when exiting actions mode / settings save).
    private static List<ActionItem>? _cache;

    private static List<ActionItem> LoadActions()
    {
        if (_cache != null) return _cache;
        try { _cache = PluginSettingsService.GetSetting<List<ActionItem>>("SwiftList.Plugins.CustomActions", "Actions", null!) ?? new(); }
        catch { _cache = new(); }
        return _cache;
    }

    public static void InvalidateCache() => _cache = null;

    public bool CanProvide(ISearchResult result) => true;

    public bool IsVisibleInMenu(ISearchResult result, SearchWindowType windowType)
        => LoadActions().Any(a => IsApplicable(a, result));

    public IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(ISearchResult result)
    {
        foreach (var cmd in LoadActions())
        {
            if (!IsApplicable(cmd, result) || string.IsNullOrWhiteSpace(cmd.Hotkey)) continue;
            var c = cmd;
            var r = result;
            yield return (c.Hotkey, () => Run(c, r));
        }
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu)
    {
        if (hMenu != IntPtr.Zero) yield break;

        foreach (var cmd in LoadActions())
        {
            if (!IsApplicable(cmd, result)) continue;

            var capturedCmd = cmd;
            var capturedResult = result;
            var iconPath = string.IsNullOrWhiteSpace(cmd.Icon) ? DefaultIcon : cmd.Icon.Trim();

            yield return new DynamicMenuItem
            {
                Text = cmd.Title,
                CommandId = 0,
                ShortcutHint = cmd.Hotkey,
                OnExecute = () => Run(capturedCmd, capturedResult)
            };
        }
    }

    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd) { }

    public void ClearSession() => _cache = null;

    private static bool IsApplicable(ActionItem cmd, ISearchResult result)
    {
        if (!cmd.Enabled) return false;
        if (string.IsNullOrWhiteSpace(cmd.Title) || string.IsNullOrWhiteSpace(cmd.Path)) return false;
        if (cmd.FolderOnly && !result.IsDir) return false;

        if (!result.IsDir && !string.IsNullOrWhiteSpace(cmd.Extensions))
        {
            var ext = Path.GetExtension(result.FullPath ?? "").ToLowerInvariant();
            var allowed = cmd.Extensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant());
            if (!allowed.Contains(ext)) return false;
        }

        return true;
    }

    private static void Run(ActionItem cmd, ISearchResult result)
    {
        // The action runs on the selected result, so there is only one substitution value:
        // its full path. %s and {} are interchangeable placeholders for it. We quote the
        // value ourselves so it stays a single argument (paths with spaces / trailing
        // backslashes) — users must NOT wrap the placeholder in quotes themselves.
        var quotedPath = ArgQuoting.Quote(result.FullPath);
        var param = string.IsNullOrWhiteSpace(cmd.Parameter) ? quotedPath
            : cmd.Parameter.Replace("%s", quotedPath).Replace("{}", quotedPath);

        var workDir = cmd.WorkingDir;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = result.IsDir ? result.FullPath : Path.GetDirectoryName(result.FullPath) ?? "";

        var psi = new ProcessStartInfo
        {
            FileName = cmd.Path,
            Arguments = param,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir))
            psi.WorkingDirectory = workDir;
        if (cmd.RunSilently) { psi.WindowStyle = ProcessWindowStyle.Hidden; psi.CreateNoWindow = true; }
        if (cmd.RunAsAdmin) psi.Verb = "runas";

        try { Process.Start(psi); }
        catch { }
    }
}
