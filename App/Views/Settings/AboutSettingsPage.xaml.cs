using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SwiftList.App.Services;
using SwiftList.PluginSdk;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;

namespace SwiftList.App.Views.Settings
{
    public partial class AboutSettingsPage : System.Windows.Controls.UserControl
    {
        private GitHubReleaseInfo? _latestRelease;

        public string AppVersion
        {
            get
            {
                var version = typeof(AboutSettingsPage).Assembly.GetName().Version;
                return string.Format(TranslationManager.Instance["About_Version"], version?.ToString(3));
            }
        }

        public AboutSettingsPage()
        {
            InitializeComponent();
            DataContext = this;
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Checking"];
            SpUpdateActions.Visibility = Visibility.Collapsed;
            TxtNoAdminWarning.Visibility = Visibility.Collapsed;

            GitHubReleaseInfo? release = null;
            try
            {
                release = await UpdateService.Instance.CheckForUpdatesAsync();
                BtnCheckUpdate.IsEnabled = true;

                if (release == null)
                {
                    TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
                    string msg = TranslationManager.Instance["About_CheckUpdateNull"];
                    string title = TranslationManager.Instance["About_CheckUpdateStatusTitle"];
                    MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _latestRelease = release;
            }
            catch (Exception ex)
            {
                BtnCheckUpdate.IsEnabled = true;
                TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
                string msgFormat = TranslationManager.Instance["About_CheckUpdateError"];
                string msg = string.Format(msgFormat, ex.Message, ex.StackTrace);
                string title = TranslationManager.Instance["About_CheckUpdateErrorTitle"];
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Compare versions
            var currentVersion = typeof(AboutSettingsPage).Assembly.GetName().Version;
            var cleanTag = release.TagName.TrimStart('v', 'V');
            if (Version.TryParse(cleanTag, out var latestVersion))
            {
                if (latestVersion > currentVersion)
                {
                    var newVerFormat = TranslationManager.Instance["About_NewVersionAvailable"];
                    TxtUpdateStatus.Text = string.Format(newVerFormat, release.TagName);
                    
                    // Show update actions
                    SpUpdateActions.Visibility = Visibility.Visible;

                    // If user is not admin, show warning and disable auto-update button
                    if (!UpdateService.Instance.IsUserAdmin())
                    {
                        TxtNoAdminWarning.Visibility = Visibility.Visible;
                        BtnAutoUpdate.IsEnabled = false;
                    }
                    else
                    {
                        BtnAutoUpdate.IsEnabled = true;
                    }
                }
                else
                {
                    TxtUpdateStatus.Text = TranslationManager.Instance["About_UpToDate"];
                }
            }
            else
            {
                TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
            }
        }

        private async void BtnAutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_latestRelease == null) return;

            // Find portable zip asset
            var zipAsset = _latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset == null)
            {
                TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
                return;
            }

            BtnCheckUpdate.IsEnabled = false;
            BtnAutoUpdate.IsEnabled = false;
            BtnGoToPage.IsEnabled = false;
            PbUpdate.Visibility = Visibility.Visible;
            PbUpdate.Value = 0;

            var downloadingFormat = TranslationManager.Instance["About_Downloading"];

            var success = await UpdateService.Instance.StartSilentUpdateAsync(zipAsset.BrowserDownloadUrl, (progress) =>
            {
                Dispatcher.Invoke(() =>
                {
                    PbUpdate.Value = progress * 100;
                    TxtUpdateStatus.Text = string.Format(downloadingFormat, (int)(progress * 100));
                });
            });

            if (success)
            {
                TxtUpdateStatus.Text = TranslationManager.Instance["About_Success"];
                // Quit App so batch script can replace files
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                PbUpdate.Visibility = Visibility.Collapsed;
                BtnCheckUpdate.IsEnabled = true;
                BtnAutoUpdate.IsEnabled = true;
                BtnGoToPage.IsEnabled = true;
                TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
            }
        }

        private void BtnGoToPage_Click(object sender, RoutedEventArgs e)
        {
            if (_latestRelease == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _latestRelease.HtmlUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SwiftList.Core.Logger.Log($"[AboutSettingsPage] Failed to open URL: {ex.Message}", SwiftList.Core.LogLevel.Warn);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                SwiftList.Core.Logger.Log($"[AboutSettingsPage] Failed to open URL: {ex.Message}", SwiftList.Core.LogLevel.Warn);
            }
        }
    }
}
