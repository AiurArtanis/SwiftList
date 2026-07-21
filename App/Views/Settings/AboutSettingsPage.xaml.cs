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

    // Tracks which brush key is currently showing, since ServiceStatusBrush is a plain bound CLR
    // property (not a DependencyProperty) -- unlike SetResourceReference on a UI element, its value
    // never re-resolves on its own when the theme changes, so ThemeChanged below re-runs this lookup
    // manually against whatever status was last determined.
    private string? _serviceStatusBrushKey;
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

    public string CliVersion
    {
        get
        {
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slf.dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dllPath);
                    var version = assemblyName.Version;
                    if (version != null)
                    {
                        return string.Format(TranslationManager.Instance["About_CliVersion"], version.ToString(3));
                    }
                }
                catch
                {
                    // Fallback
                }
            }
            return string.Format(TranslationManager.Instance["About_CliVersion"], "Unknown");
        }
    }

    // The user manual link's target differs per locale (/zh-CN/ prefix), so it's derived from the
    // same translated URL string the link displays rather than a single hardcoded Uri like the
    // (locale-independent) homepage link above it.
    public Uri? UserGuideUri => Uri.TryCreate(TranslationManager.Instance["About_UserGuideUrl"], UriKind.Absolute, out var uri) ? uri : null;

    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += AboutSettingsPage_Loaded;

        // AppVersion/CoreVersion/ServiceVersion embed a translated format string, so they need to
        // re-resolve (not just the XAML-bound About_* labels) when the language changes at runtime.
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(AppVersion));
            OnPropertyChanged(nameof(CoreVersion));
            OnPropertyChanged(nameof(ServiceVersion));
            OnPropertyChanged(nameof(CliVersion));
            OnPropertyChanged(nameof(UserGuideUri));
        };

        ThemeManager.Instance.ThemeChanged += UpdateServiceStatusBrush;
        Unloaded += (_, _) => ThemeManager.Instance.ThemeChanged -= UpdateServiceStatusBrush;
    }

    private void AboutSettingsPage_Loaded(object sender, RoutedEventArgs e) => CheckServiceStatus();

    private async void CheckServiceStatus()
    {
        try
        {
            using var searchService = new SearchService();
            var status = await searchService.GetStatusAsync();
            _serviceStatusBrushKey = status != null && status.State != "error" ? "SuccessBadgeText" : "ErrorBrush";
        }
        catch
        {
            _serviceStatusBrushKey = "ErrorBrush";
        }
        UpdateServiceStatusBrush();
    }

    private void UpdateServiceStatusBrush()
    {
        if (_serviceStatusBrushKey == null) return;
        var fallback = _serviceStatusBrushKey == "ErrorBrush" ? Brushes.Red : Brushes.Green;
        ServiceStatusBrush = System.Windows.Application.Current.TryFindResource(_serviceStatusBrushKey) as Brush ?? fallback;
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
        Helpers.UrlLauncher.Open(_latestRelease.HtmlUrl);
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Helpers.UrlLauncher.Open(e.Uri);
        e.Handled = true;
    }

}
