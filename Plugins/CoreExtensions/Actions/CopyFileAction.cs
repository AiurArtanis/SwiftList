using System;
using System.Windows.Media;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.Core;

namespace SwiftList.Plugins.CoreExtensions.Actions
{
    public class CopyFileAction : ISearchResultAction
    {
        public string GroupName => TranslationService.Get("Action_BuiltinGroup");

        public string DisplayName => TranslationService.Get("Action_Copy");

        public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
            "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z", 
            "TextPrimary");

        public bool CanExecute(ISearchResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
            string physicalPath = NetworkDriveResolver.ResolveToPhysicalPath(result.FullPath);
            return System.IO.File.Exists(physicalPath) || System.IO.Directory.Exists(physicalPath);
        }

        public void Execute(ISearchResult result, IPluginSearchWindow view)
        {
            try
            {
                var fileList = new System.Collections.Specialized.StringCollection { result.FullPath };
                System.Windows.Clipboard.SetFileDropList(fileList);
            }
            catch (Exception ex)
            {
                Logger.Log($"[CopyFileAction] Failed to copy file: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
        }
    }
}
