using System.Windows.Media;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Services;
using SwiftList.PluginSdk.Helpers;

namespace SwiftList.Plugins.CoreExtensions.Actions;

public class LocateInExplorerAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_LocateFolder");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8L10 4z M20 18H4V8h16v10z",
        "TextPrimary");

    public bool CanExecute(ISearchResult result) => result != null && !string.IsNullOrEmpty(result.FullPath);

    public void Execute(ISearchResult result, IPluginSearchWindow view) => view.LocateInExplorerExternal(result.FullPath);
}
