using SwiftList.Plugins.CoreExtensions.Actions;
using SwiftList.Plugins.CoreExtensions.Shell;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
namespace SwiftList.Plugins.CoreExtensions;

public class CoreExtensionsPlugin : IPlugin, IActionProvider, IConfigurable
{
    public string Name => TranslationService.Get("Plugins_CoreActionPluginName");
    public string Description => TranslationService.Get("CoreExtensions_PluginDesc");

    public IEnumerable<ISearchResultAction> GetActions() => new ISearchResultAction[]
        {
            new OpenResultAction(),
            new OpenResultAsAdminAction(),
            new LocateInExplorerAction(),
            new CopyPathAction(),
            new CutFileAction(),
            new CopyFileAction(),
            new PasteFileAction(),
            new DeleteFileAction(),
            new PermanentDeleteFileAction(),
            new OpenCommandPromptAction(),
            new OpenAdminCommandPromptAction(),
            new TouchAction(),
            new MkdirAction()
        };

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => new IDynamicActionProvider[]
        {
            new ShellMenuActionProvider()
        };

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "ThumbnailGroup",
                LabelKey = "CoreExtensions_Config_ThumbnailGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "ThumbnailExtensions",
                        LabelKey = "CoreExtensions_Config_ThumbnailExtensionsLabel",
                        DescriptionKey = "CoreExtensions_Config_ThumbnailExtensionsDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = new List<string>
                        {
                            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
                            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".m4v"
                        }
                    }
                }
            },
            new PluginConfigField
            {
                Key = "CustomFoldersGroup",
                LabelKey = "CoreExtensions_Config_CustomFoldersGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "CustomFolders",
                        LabelKey = "CoreExtensions_Config_CustomFoldersLabel",
                        DescriptionKey = "CoreExtensions_Config_CustomFoldersDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = new List<string>()
                    }
                }
            },
            new PluginConfigField
            {
                Key = "SearchSettingsTrigger",
                LabelKey = "CoreExtensions_Config_SearchSettingsTriggerLabel",
                DescriptionKey = "CoreExtensions_Config_SearchSettingsTriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "set"
            }
        }
    };
}
