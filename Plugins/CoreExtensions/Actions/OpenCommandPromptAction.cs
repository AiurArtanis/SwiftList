using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;

namespace SwiftList.Plugins.CoreExtensions.Actions;

public class OpenCommandPromptAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenCmd");

    public IReadOnlyList<string> Keywords => new[] { "cmd" };

    public bool IsVisibleInSearch(ISearchResult result, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(ISearchResult result, SearchWindowType windowType) => result != null && result.IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(ISearchResult result) => result != null && !string.IsNullOrWhiteSpace(result.ContextDirectory) && Directory.Exists(result.ContextDirectory);

    public void Execute(ISearchResult result, IPluginSearchWindow view) => CommandPromptLauncher.Open(result.FullPath, result.ContextDirectory, runAsAdmin: false);
}

public class OpenAdminCommandPromptAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenAdminCmd");

    public IReadOnlyList<string> Keywords => new[] { "cmda" };

    public bool IsVisibleInSearch(ISearchResult result, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(ISearchResult result, SearchWindowType windowType) => result != null && result.IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(ISearchResult result) => result != null && !string.IsNullOrWhiteSpace(result.ContextDirectory) && Directory.Exists(result.ContextDirectory);

    public void Execute(ISearchResult result, IPluginSearchWindow view) => CommandPromptLauncher.Open(result.FullPath, result.ContextDirectory, runAsAdmin: true);
}

internal static class CommandPromptLauncher
{
    public static void Open(string pathText, string contextDirectory, bool runAsAdmin)
    {
        var workingDirectory = ResolveWorkingDirectory(pathText, contextDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/K cd /d \"{workingDirectory}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        if (runAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }

    private static string ResolveWorkingDirectory(string pathText, string contextDirectory)
    {
        var path = (pathText ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResolveFallbackDirectory(contextDirectory);
        }

        try
        {
            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (File.Exists(path))
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    return parent;
                }
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.HasExtension(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }
        catch
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string ResolveFallbackDirectory(string contextDirectory)
    {
        var directory = (contextDirectory ?? string.Empty).Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            return Path.GetFullPath(directory);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
