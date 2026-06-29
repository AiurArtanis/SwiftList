using System.IO;
using System.Windows;
using SwiftList.PluginSdk.Services;
using SwiftList.App.Services;

namespace SwiftList.App.Views.QuickLook;

public partial class QuickLookWindow : Window
{
    private string? _currentFilePath;

    public QuickLookWindow() => InitializeComponent();

    public void SetTarget(string path)
    {
        if (_currentFilePath == path) return;
        _currentFilePath = path;

        ContentArea.Content = null;

        if (string.IsNullOrEmpty(path))
        {
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
            TxtFileName.Text = Path.GetFileName(path);
            if (string.IsNullOrEmpty(TxtFileName.Text) && isDir) TxtFileName.Text = path;

            TxtFilePath.Text = path;
            ImgFileIcon.Source = ShellIconHelper.GetIconForPath(path, isDir);

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

            // Query preview provider from plugins
            var provider = PluginManager.Instance.FilePreviewProviders
                .FirstOrDefault(p => p.CanPreview(path, isDir));

            if (provider != null)
            {
                ContentArea.Content = provider.CreatePreview(path, isDir);
            }
        }
        catch (Exception ex)
        {
            var errTxt = new System.Windows.Controls.TextBlock
            {
                Text = $"{TranslationService.Get("QuickLook_Error")}: {ex.Message}",
                Foreground = System.Windows.Media.Brushes.Red,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };
            ContentArea.Content = errTxt;
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
