using System.Windows.Media;
using SwiftList.PluginSdk;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;
using SwiftList.Plugins.CoreExtensions.Shell;

namespace SwiftList.Plugins.CoreExtensions.Actions;

public class DeleteFileAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_Delete");

    public string Description => TranslationService.Get("Action_Delete_Desc");

    // Matches Explorer's own Delete key; the native Recycle Bin confirmation prompt (IFileOperation,
    // FOF_ALLOWUNDO) is the actual safeguard against an accidental press, not withholding the action.
    public string Hotkey => "Delete";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z",
        "TextPrimary");

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
        ShellDeleteHelper.DeleteAsync(paths, permanent: false);
    }
}
