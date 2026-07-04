using System.IO;
using System.Windows.Media;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;
namespace SwiftList.Plugins.CoreExtensions.Actions;

public class OpenResultAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_Open");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(

        "M8 5v14l11-7L8 5z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(HasExistingPath);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        foreach (var result in results)
            view.OpenFileOrFolderExternal(result.FullPath);
    }

    internal static bool HasExistingPath(ISearchResult result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.FullPath))
            return false;
        if (result.IsApplication)
            return false;
        return File.Exists(result.FullPath) || Directory.Exists(result.FullPath);
    }
}

public class OpenResultAsAdminAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_OpenAdmin");

    // Built-in hotkey; the search windows dispatch it through HotkeyActionTrigger instead of hardcoding.
    public string Hotkey => "Ctrl+Shift+Enter";

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(

        "M12 2 4 5v6c0 5 3.4 9.7 8 11 4.6-1.3 8-6 8-11V5l-8-3zm0 3.2 5 1.9V11c0 3.4-2 6.7-5 8-3-1.3-5-4.6-5-8V7.1l5-1.9z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(r => OpenResultAction.HasExistingPath(r) && !r.IsDir);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        foreach (var result in results)
            view.OpenFileOrFolderAsAdminExternal(result.FullPath);
    }
}
