using System.Diagnostics;
using System.IO;
using System.Windows;
using System.ComponentModel;
using SwiftList.App.Services;
using SwiftList.Core;
using MessageBox = SwiftList.App.Views.Controls.CustomMessageBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace SwiftList.App.Views.Settings;

public partial class AboutSettingsPage : System.Windows.Controls.UserControl, INotifyPropertyChanged
{
    private GitHubReleaseInfo? _latestRelease;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private Brush _serviceStatusBrush = Brushes.Gray;
    public Brush ServiceStatusBrush
    {
        get => _serviceStatusBrush;
        private set
        {
            if (_serviceStatusBrush != value)
            {
                _serviceStatusBrush = value;
                OnPropertyChanged(nameof(ServiceStatusBrush));
            }
        }
    }

    public string AppVersion
    {
        get
        {
            var version = typeof(AboutSettingsPage).Assembly.GetName().Version;
            return string.Format(TranslationManager.Instance["About_Version"], version?.ToString(3));
        }
    }

    public string CoreVersion
    {
        get
        {
            var version = typeof(Logger).Assembly.GetName().Version;
            return string.Format(TranslationManager.Instance["About_CoreVersion"], version?.ToString(3));
        }
    }

    public string ServiceVersion
    {
        get
        {
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwiftList.Service.dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dllPath);
                    var version = assemblyName.Version;
                    if (version != null)
                    {
                        return string.Format(TranslationManager.Instance["About_ServiceVersion"], version.ToString(3));
                    }
                }
                catch
                {
                    // Fallback
                }
            }
            return string.Format(TranslationManager.Instance["About_ServiceVersion"], "Unknown");
        }
    }

    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += AboutSettingsPage_Loaded;
    }

    private void AboutSettingsPage_Loaded(object sender, RoutedEventArgs e) => CheckServiceStatus();

    private async void CheckServiceStatus()
    {
        try
        {
            using var searchService = new SearchService();
            var status = await searchService.GetStatusAsync();
            if (status != null && status.State != "error")
            {
                ServiceStatusBrush = System.Windows.Application.Current.TryFindResource("SuccessBadgeText") as Brush ?? Brushes.Green;
            }
            else
            {
                ServiceStatusBrush = System.Windows.Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red;
            }
        }
        catch
        {
            ServiceStatusBrush = System.Windows.Application.Current.TryFindResource("ErrorBrush") as Brush ?? Brushes.Red;
        }
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
                var msg = TranslationManager.Instance["About_CheckUpdateNull"];
                var title = TranslationManager.Instance["About_CheckUpdateStatusTitle"];
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _latestRelease = release;
        }
        catch (Exception ex)
        {
            BtnCheckUpdate.IsEnabled = true;
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
            var msgFormat = TranslationManager.Instance["About_CheckUpdateError"];
            var msg = string.Format(msgFormat, ex.Message, ex.StackTrace);
            var title = TranslationManager.Instance["About_CheckUpdateErrorTitle"];
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

        var success = await UpdateService.Instance.StartSilentUpdateAsync(zipAsset.BrowserDownloadUrl, (progress) => Dispatcher.Invoke(() =>
            {
                PbUpdate.Value = progress * 100;
                TxtUpdateStatus.Text = string.Format(downloadingFormat, (int)(progress * 100));
            }));

        if (success)
        {
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Success"];
            // Quit App so batch script can replace files
            TrayCleanExitHelper.CleanExit();
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
            Logger.Log($"[AboutSettingsPage] Failed to open URL: {ex.Message}", LogLevel.Warn);
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
            Logger.Log($"[AboutSettingsPage] Failed to open URL: {ex.Message}", LogLevel.Warn);
        }
    }

    private void BtnStartTutorial_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwiftList.Tutorial.exe");
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(
                    $"SwiftList.Tutorial.exe was not found at {exePath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
