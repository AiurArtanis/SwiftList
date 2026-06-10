using System;
using System.Windows.Media;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Actions
{
    public class CopyPathAction : ISearchResultAction
    {
        public string GroupName => TranslationService.Get("Action_BuiltinGroup");

        public string DisplayName => TranslationService.Get("Action_CopyPath");

        public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
            "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z",
            "TextPrimary");

        public bool CanExecute(ISearchResult result)
        {
            return result != null && !string.IsNullOrEmpty(result.FullPath);
        }

        public void Execute(ISearchResult result, IPluginSearchWindow view)
        {
            try
            {
                System.Windows.Clipboard.SetText(result.FullPath);
            }
            catch (Exception ex)
            {
                Logger.Log($"[CopyPathAction] Failed to copy path: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
