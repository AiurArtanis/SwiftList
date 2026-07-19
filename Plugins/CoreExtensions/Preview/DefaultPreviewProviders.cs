using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;
namespace SwiftList.Plugins.CoreExtensions.Preview;
// 1. Folder Preview Provider
public class FolderPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_FolderProviderName");
    public int Priority => 10;
    public bool CanPreview(string path, bool isDir) => isDir;
    private readonly record struct FolderRowData(string Name, string FullPath, bool IsDir, ImageSource? Icon, bool NeedsIconLoad);

    public UIElement CreatePreview(string path, bool isDir)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(4) };
        scroll.Content = panel;

        // EnumerateFileSystemInfos hits the disk/network per call -- over a network drive with many
        // entries this blocked the whole window until it finished (same class of bug as the thumbnail one
        // above). Data gathering (no WPF elements -- those are thread-affine and can't be created off the
        // UI thread) happens in the background; the rows themselves are only ever built on the UI thread,
        // once, from that data. Each row's icon starts as whatever's already cached (instant, see
        // GetIconFromCacheOnly) and upgrades itself in place once the real one loads (see BuildRow) --
        // same cache-first-then-upgrade pattern AppSearchResult.Icon already uses for the results grid.
        Task.Run(() => CollectRows(path)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion)
            {
                panel.Children.Add(BuildMessageRow($"{TranslationService.Get("QuickLook_Error")}: {t.Exception?.GetBaseException().Message}", isError: true));
                return;
            }

            var (rows, truncatedCount) = t.Result;
            if (rows.Count == 0)
            {
                panel.Children.Add(BuildMessageRow(TranslationService.Get("QuickLook_FolderEmpty"), isError: false));
                return;
            }

            foreach (var row in rows)
                panel.Children.Add(BuildRow(row));

            if (truncatedCount > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = TranslationService.Get("QuickLook_MoreItems"),
                    Foreground = Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
                    Margin = new Thickness(24, 4, 0, 0),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic
                });
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());

        return scroll;
    }

    // Runs entirely off the UI thread -- returns plain data (icons are already-frozen ImageSources, safe
    // to hand across threads) for the UI-thread continuation above to turn into rows. Icons use the
    // cache-only fast path (no disk/shell access) so a folder full of not-yet-cached items (videos
    // especially) doesn't just move the same blocking cost from "before any row appears" to "before this
    // one Task.Run resolves" -- BuildRow below kicks off the real per-item fetch afterward instead.
    private static (List<FolderRowData> Rows, int TruncatedCount) CollectRows(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var items = dirInfo.EnumerateFileSystemInfos().Take(31).ToList();
        var displayCount = Math.Min(items.Count, 30);
        var rows = new List<FolderRowData>(displayCount);
        for (var idx = 0; idx < displayCount; idx++)
        {
            var item = items[idx];
            var isItemDir = (item.Attributes & FileAttributes.Directory) != 0;
            var icon = IconService.GetIconFromCacheOnly(item.FullName, isItemDir, out var needsLoad);
            rows.Add(new FolderRowData(item.Name, item.FullName, isItemDir, icon, needsLoad));
        }
        return (rows, items.Count > 30 ? items.Count - 30 : 0);
    }

    // Builds one row with whatever icon CollectRows already had cached, and -- only if that was just a
    // placeholder -- fetches the real one in the background and swaps it in once ready. No staleness guard
    // needed: img belongs only to this row's own Image control, which is either still showing (correct) or
    // long gone from the visual tree (harmless no-op) by the time the fetch resolves.
    private static UIElement BuildRow(FolderRowData row)
    {
        var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var img = new Image
        {
            Source = row.Icon,
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0)
        };
        rowPanel.Children.Add(img);
        rowPanel.Children.Add(new TextBlock
        {
            Text = row.Name,
            Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            FontSize = 12
        });

        if (row.NeedsIconLoad)
        {
            Task.Run(() => IconService.GetIcon(row.FullPath, row.IsDir)).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                    img.Source = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return rowPanel;
    }

    private static TextBlock BuildMessageRow(string text, bool isError) => new()
    {
        Text = text,
        FontStyle = isError ? FontStyles.Normal : FontStyles.Italic,
        Foreground = isError ? Brushes.Red : Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8)
    };
}
// 2. Image Preview Provider
public class ImagePreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_ImageProviderName");
    public int Priority => 20;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        string[] imgExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" };
        return imgExts.Contains(ext);
    }
    public UIElement CreatePreview(string path, bool isDir)
    {
        var grid = new Grid();
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current?.TryFindResource("SeparatorBrush") as Brush ?? Brushes.Gray,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        grid.Children.Add(border);
        var img = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        border.Child = img;
        try
        {
            var bmi = new BitmapImage();
            bmi.BeginInit();
            bmi.CacheOption = BitmapCacheOption.OnLoad;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bmi.StreamSource = stream;
                bmi.EndInit();
            }
            bmi.Freeze();
            img.Source = bmi;
        }
        catch
        {
            border.Child = new TextBlock { Text = "Failed to load image", Foreground = Brushes.Red, Margin = new Thickness(8) };
        }
        return grid;
    }
}
// 3. Text Preview Provider
public class TextPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_TextProviderName");
    public int Priority => 5;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        string[] txtExts = {
            ".txt", ".log", ".cs", ".xml", ".json", ".md", ".js", ".ts", ".py",
            ".html", ".css", ".ini", ".cfg", ".bat", ".cmd", ".sh", ".yml",
            ".yaml", ".sql", ".csproj", ".sln", ".config", ".properties"
        };
        if (txtExts.Contains(ext)) return true;
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length < 102400) return true; // Under 100KB, try previewing
        }
        catch { }
        return false;
    }
    public UIElement CreatePreview(string path, bool isDir)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        };
        var txt = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12.5,
            Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
            Margin = new Thickness(4),
            TextWrapping = TextWrapping.Wrap
        };
        scroll.Content = txt;
        try
        {
            var buffer = new byte[4096];
            int bytesRead;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                bytesRead = fs.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
                }
            }
            string textContent;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                var chars = new char[1500];
                var read = reader.ReadBlock(chars, 0, chars.Length);
                textContent = new string(chars, 0, read);
                if (reader.Peek() >= 0) textContent += "\r\n" + TranslationService.Get("QuickLook_Truncated");
            }
            txt.Text = textContent;
        }
        catch (Exception ex)
        {
            txt.Text = $"Error loading text: {ex.Message}";
        }
        return scroll;
    }
}
// 4. PE Executable Preview Provider
public class PePreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_PeProviderName");
    public int Priority => 15;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".exe" || ext == ".dll";
    }
    public UIElement CreatePreview(string path, bool isDir)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var arch = GetPeArchitecture(path);
            var desc = !string.IsNullOrEmpty(versionInfo.FileDescription) ? versionInfo.FileDescription : TranslationService.Get("QuickLook_PeExecutable");
            var ver = !string.IsNullOrEmpty(versionInfo.ProductVersion) ? versionInfo.ProductVersion : versionInfo.FileVersion ?? "Unknown version";
            var details = $"{TranslationService.Get("QuickLook_Version")}: {ver}\n" +
                             $"{TranslationService.Get("QuickLook_Architecture")}: {arch}\n" +
                             $"{TranslationService.Get("QuickLook_Company")}: {versionInfo.CompanyName ?? "N/A"}\n" +
                             $"{TranslationService.Get("QuickLook_Product")}: {versionInfo.ProductName ?? "N/A"}";
            return BuildMetadataControl(path, desc, details);
        }
        catch
        {
            return new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
        }
    }
    private string GetPeArchitecture(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            fs.Seek(0x3c, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            var peHead = br.ReadUInt32();
            if (peHead == 0x00004550)
            {
                var machineType = br.ReadUInt16();
                return machineType switch
                {
                    0x014c => "x86 (32-bit)",
                    0x8664 => "x64 (64-bit)",
                    0xaa64 => "ARM64",
                    _ => "Unknown (" + machineType.ToString("X") + ")"
                };
            }
        }
        catch { }
        return "Unknown Architecture";
    }
    public static UIElement BuildMetadataControl(string path, string? title, string? details, ImageSource? image = null)
        => BuildMetadataControl(path, title, details, image, out _);

    // imageElement: the actual Image control used for the icon/thumbnail slot, so a caller that built this
    // with image=null (a placeholder) can restyle it into the "real thumbnail" layout later once one loads
    // asynchronously, instead of only being able to swap Source (which would leave a large thumbnail stuck
    // rendering at the small placeholder icon's fixed 64x64 box).
    public static UIElement BuildMetadataControl(string path, string? title, string? details, ImageSource? image, out Image imageElement)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(16) };
        var panel = new StackPanel();
        grid.Children.Add(panel);
        Image img;
        if (image != null)
        {
            // Real thumbnail — stretch to fill the pane width (keeping aspect), capped in height.
            img = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 420,
                Margin = new Thickness(0, 0, 0, 16)
            };
        }
        else
        {
            // No thumbnail (generic file / executable) — a small centered shell icon, not an upscaled blur.
            img = new Image
            {
                Source = IconService.GetIcon(path, false),
                Width = 64,
                Height = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
        }
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        imageElement = img;
        panel.Children.Add(img);
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }
        if (!string.IsNullOrEmpty(details))
        {
            panel.Children.Add(new TextBlock
            {
                Text = details,
                TextAlignment = TextAlignment.Center,
                FontSize = 12,
                Foreground = Application.Current?.TryFindResource("TextSecondary") as Brush ?? Brushes.Gray
            });
        }
        return grid;
    }
}
// 5. Default Fallback Metadata Preview Provider
public class DefaultMetadataPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_MetadataProviderName");
    public int Priority => 1;
    public bool CanPreview(string path, bool isDir) => true;
    public UIElement CreatePreview(string path, bool isDir)
    {
        // Filename and size/date already live in the QuickLook header and footer, so this fallback shows
        // just a large real thumbnail (video frame / document page / image), or a small shell icon if none.
        //
        // GetThumbnail is a synchronous shell COM call -- for a video file on a network drive, the shell
        // has to actually read/decode frame data over the network to produce it, which can take seconds
        // and, called here, blocked the whole window (and the search window under it) until it returned.
        // Show the small-icon placeholder layout immediately instead, then fetch the real thumbnail in the
        // background and restyle the same Image element into the "real thumbnail" layout once it arrives.
        var control = PePreviewProvider.BuildMetadataControl(path, null, null, null, out var img);
        Task.Run(() => IconService.GetThumbnail(path, 512)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
            // No staleness check needed: img belongs only to this specific control instance. If the user
            // has since navigated away, this control (and img) is simply no longer in the visual tree, and
            // restyling it is a harmless no-op rather than something that could show a stale result.
            img.Source = t.Result;
            img.Stretch = Stretch.Uniform;
            img.HorizontalAlignment = HorizontalAlignment.Stretch;
            img.MaxHeight = 420;
            img.Width = double.NaN;
            img.Height = double.NaN;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return control;
    }
}
