namespace SwiftList.App.Views.Settings.Plugins;

public partial class PluginManagementSettingsPage : System.Windows.Controls.UserControl
{
    public PluginManagementSettingsPage() => InitializeComponent();

    private void SdkBadge_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Settings.Plugins.PluginManagementViewModel vm && vm.DevGuideUri != null)
            Helpers.UrlLauncher.Open(vm.DevGuideUri);
    }
}
