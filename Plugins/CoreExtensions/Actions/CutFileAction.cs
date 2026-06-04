using System;
using System.Windows.Media;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using SwiftList.Core;

namespace SwiftList.Plugins.CoreExtensions.Actions
{
    public class CutFileAction : ISearchResultAction
    {
        public string GroupName => TranslationService.Get("Action_BuiltinGroup");

        public string DisplayName => TranslationService.Get("Action_Cut");

        public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
            "M9.64 7.64c.23-.5.36-1.05.36-1.64 0-2.2-1.8-4-4-4S2 3.8 2 6s1.8 4 4 4c.59 0 1.14-.13 1.64-.36L10 12l-2.36 2.36C7.14 14.13 6.59 14 6 14c-2.2 0-4 1.8-4 4s1.8 4 4 4 4-1.8 4-4c0-.59-.13-1.14-.36-1.64L12 14l7 7h3v-1L12 12l10-10V1h-3l-7.36 6.64zM6 8c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm0 12c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z", 
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
                var data = new System.Windows.DataObject();
                data.SetFileDropList(fileList);
                
                byte[] effect = new byte[] { (byte)System.Windows.DragDropEffects.Move, 0, 0, 0 };
                var stream = new System.IO.MemoryStream(effect);
                data.SetData("Preferred DropEffect", stream);
                
                System.Windows.Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CutFileAction] Failed to cut file: {ex.Message}");
            }
        }
    }
}
