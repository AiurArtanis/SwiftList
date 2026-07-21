using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
using SwiftList.App.Services;

namespace SwiftList.App.Views.QuickLook;

public partial class QuickLookWindow : Window
{
    // Must match QuickLookWindow.xaml's WindowBorder Margin -- the invisible gap between the window's
    // outer (transparent, drop-shadow) bounds and the actual visible card, on every side.
    public const double ContentMargin = 12;

    private string? _currentFilePath;
    private readonly PreviewOverlay _overlay;
    private HwndHost? _pendingHost;
    private UIElement? _currentPreview;
    private IFilePreviewProvider? _currentProvider;

    public QuickLookWindow()
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        _overlay = new PreviewOverlay(this, ContentArea);
        IsVisibleChanged += (s, e) =>
        {
            if (!IsVisible)
            {
                // Release a hosted native preview (HwndHost -> IPreviewHandler + its prevhost surrogate
                // and file lock) whenever the window hides; the next show rebuilds it.
                ReleasePreview();
                _currentFilePath = null;
            }
            else if (_pendingHost != null)
            {
                // Overlay needs the window shown before it can be Owner-ed; attach it now.
                var host = _pendingHost;
                _pendingHost = null;
                _overlay.Show(host);
            }
        };
    }

    private void ReleasePreview()
    {
        _overlay.Clear();
        (_pendingHost as IDisposable)?.Dispose();
        _pendingHost = null;
        (ContentArea.Content as IDisposable)?.Dispose();
        ContentArea.Content = null;
        _currentPreview = null;
        _currentProvider = null;
    }

    public void SetTarget(string path)
    {
        if (_currentFilePath == path) return;
        _currentFilePath = path;

        if (string.IsNullOrEmpty(path))
        {
            ReleasePreview();
            TxtFileName.Text = TranslationService.Get("QuickLook_NoSelection");
            TxtFilePath.Text = string.Empty;
            ImgFileIcon.Source = null;
            TxtFooterSize.Text = string.Empty;
            TxtFooterDate.Text = string.Empty;
            return;
        }

        try
        {
            var isDir = Directory.Exists(path);
            UpdateHeader(path, isDir);

            // Priority-based selection stays authoritative: pick the winning provider first.
            var provider = PluginManager.Instance.FilePreviewProviders
                .FirstOrDefault(p => p.CanPreview(path, isDir));

            // Only reuse in place when the SAME provider wins again and its control can re-point itself.
            // This keeps the pool from bypassing a higher-priority (or third-party) provider that should
            // own the new file, while still avoiding overlay/prevhost churn on same-type navigation.
            if (ReferenceEquals(provider, _currentProvider)
                && _currentPreview is IReusablePreview reusable && reusable.TrySetTarget(path, isDir))
                return;

            ReleasePreview();

            if (provider != null)
            {
                var content = provider.CreatePreview(path, isDir);
                _currentProvider = provider;
                _currentPreview = content;
                if (content is HwndHost host)
                {
                    // Native (HwndHost) previews can't render in this layered window — host them in a
                    // separate non-layered overlay laid over the content area. Owner requires the window
                    // to be shown, so defer until it is visible.
                    if (IsVisible) _overlay.Show(host);
                    else _pendingHost = host;
                }
                else
                {
                    ContentArea.Content = content;
                }
            }
        }
        catch (Exception ex)
        {
            ReleasePreview();
            var errTxt = new System.Windows.Controls.TextBlock
            {
                Text = $"{TranslationService.Get("QuickLook_Error")}: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };
            errTxt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ErrorBrush");
            ContentArea.Content = errTxt;
        }
    }

    private void UpdateHeader(string path, bool isDir)
    {
        TxtFileName.Text = Path.GetFileName(path);
        if (string.IsNullOrEmpty(TxtFileName.Text) && isDir) TxtFileName.Text = path;

        TxtFilePath.Text = path;

        // Cache-only fast path first (matches AppSearchResult.Icon's pattern) -- a network-drive video
        // file's real thumbnail requires the shell to actually read/decode frame data over the network,
        // which can take seconds; calling GetIconForPath directly here blocked this whole window (and the
        // owning search window, since QuickLook rides its message loop) until that finished. A cached hit
        // or generic placeholder shows instantly; needsLoad only fires the slow fetch in the background.
        var icon = ShellIconHelper.GetIconFromCacheOnly(path, isDir, out var needsLoad);
        ImgFileIcon.Source = icon;
        if (needsLoad)
        {
            Task.Run(() => ShellIconHelper.GetIconForPath(path, isDir)).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
                // The user may have already navigated to a different file by the time this resolves --
                // _currentFilePath is updated synchronously at the top of SetTarget before this method
                // even runs, so comparing against it here is the same staleness check, just applied late.
                if (_currentFilePath == path) ImgFileIcon.Source = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        if (isDir)
        {
            var dirInfo = new DirectoryInfo(path);
            TxtFooterSize.Text = TranslationService.Get("QuickLook_Folder");
            TxtFooterDate.Text = $"{TranslationService.Get("QuickLook_Modified")}: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm}";
        }
        else if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            TxtFooterSize.Text = FormatFileSize(fileInfo.Length);
            TxtFooterDate.Text = $"{TranslationService.Get("QuickLook_Modified")}: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}";
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double val = bytes;
        var i = 0;
        while (val >= 1024 && i < suffixes.Length - 1)
        {
            val /= 1024;
            i++;
        }
        return $"{val:0.##} {suffixes[i]}";
    }
}
