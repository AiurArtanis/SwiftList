using System;
using System.IO;
using System.Windows.Media;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions.Actions
{
    public class TouchAction : ISearchResultAction
    {
        public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

        public string DisplayName => TranslationService.Get("Action_Touch");

        public IReadOnlyList<string> Keywords => new[] { "touch" };

        public IReadOnlyList<string> Parameters => new[] { "filename" };

        public bool InlineWindowOnly => true;

        public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
            "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 12h-3v3h-2v-3H8v-2h3V9h2v3h3v2zm-3-5V3.5L18.5 9H13z",
            "TextPrimary");

        public bool CanExecute(ISearchResult result)
        {
            return result != null;
        }

        public void Execute(ISearchResult result, IPluginSearchWindow view)
        {
            if (string.IsNullOrWhiteSpace(result.FullPath))
            {
                return;
            }

            try
            {
                string targetPath = Path.Combine(result.ContextDirectory, result.FullPath.Trim());
                string? dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (!File.Exists(targetPath))
                {
                    File.WriteAllBytes(targetPath, Array.Empty<byte>());
                }
            }
            catch
            {
            }
        }
    }
}
