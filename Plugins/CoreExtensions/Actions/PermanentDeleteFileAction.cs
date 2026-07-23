using System.Windows.Media;
using SwiftList.PluginSdk;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;
using SwiftList.Plugins.CoreExtensions.Shell;

using SwiftList.Plugins.CoreExtensions.Shell.FileOperations;
namespace SwiftList.Plugins.CoreExtensions.Actions;

public class PermanentDeleteFileAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_PermanentDelete");

    public string Description => TranslationService.Get("Action_PermanentDelete_Desc");

    // Matches Explorer's own Shift+Delete. No FOF_ALLOWUNDO -- the Recycle Bin is skipped entirely,
    // so the native "permanently delete?" confirmation (not withheld here) is the only safeguard.
    public string Hotkey => "Shift+Delete";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zm2.46-7.12l1.41-1.41L12 12.59l2.12-2.12 1.41 1.41L13.41 14l2.12 2.12-1.41 1.41L12 15.41l-2.12 2.12-1.41-1.41L10.59 14l-2.13-2.12zM15.5 4l-1-1h-5l-1 1H5v2h14V4z",
        "ErrorBrush");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(Exists);

    private static bool Exists(ISearchResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return System.IO.File.Exists(result.FullPath) || System.IO.Directory.Exists(result.FullPath);
    }

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        var paths = results.Where(Exists).Select(r => r.FullPath).ToArray();
        if (paths.Length == 0) return;
        ShellDeleteHelper.DeleteAsync(paths, permanent: true);
    }
}
