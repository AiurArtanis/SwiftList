using SwiftList.Plugins.CoreExtensions.Actions;
using SwiftList.Plugins.CoreExtensions.Shell;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
namespace SwiftList.Plugins.CoreExtensions;

public class CoreExtensionsPlugin : IPlugin, IActionProvider
{
    public string Name => TranslationService.Get("Plugins_CoreActionPluginName");

    public IEnumerable<ISearchResultAction> GetActions() => new ISearchResultAction[]
        {
            new OpenResultAction(),
            new OpenResultAsAdminAction(),
            new LocateInExplorerAction(),
            new CopyPathAction(),
            new CopyFileAction(),
            new CutFileAction(),
            new OpenCommandPromptAction(),
            new OpenAdminCommandPromptAction(),
            new TouchAction(),
            new MkdirAction()

        };

    public IEnumerable<IDynamicActionProvider> GetDynamicProviders() => new IDynamicActionProvider[]
        {
            new ShellMenuActionProvider()
        };
}
