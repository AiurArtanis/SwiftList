using System.Collections.Generic;
using SwiftList.Plugins.CoreExtensions.Actions;
using SwiftList.Plugins.CoreExtensions.Shell;
using SwiftList.PluginSdk;

namespace SwiftList.Plugins.CoreExtensions
{
    public class CoreExtensionsPlugin : IActionPlugin
    {
        public string Name => TranslationService.Get("Plugins_CoreActionPluginName");

        public IEnumerable<ISearchResultAction> GetActions()
        {
            return new ISearchResultAction[]
            {
                new LocateInExplorerAction(),
                new CopyPathAction(),
                new CopyFileAction(),
                new CutFileAction(),
                new OpenCommandPromptAction(),
                new OpenAdminCommandPromptAction(),
                new TouchAction(),
                new MkdirAction()
            };
        }

        public IEnumerable<IDynamicActionProvider> GetDynamicProviders()
        {
            return new IDynamicActionProvider[]
            {
                new ShellMenuActionProvider()
            };
        }
    }
}
